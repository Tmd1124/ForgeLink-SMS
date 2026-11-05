using Android.Runtime;
using Java.Lang;
using Java.Lang.Reflect;
using ForgeLinkSms.Core.Services;
using Exception = System.Exception;

namespace ForgeLinkSms.Platforms.Android;

// ML Kit's GenAI library is Kotlin, which the .NET binder can't wrap, so it's only packaged into the app
// and called through Java reflection. Every name below comes from com.google.mlkit:genai-prompt 1.0.0-beta5.
public class OnDeviceAiService : IOnDeviceAiService
{
    private const string Pkg = "com.google.mlkit.genai.prompt.";
    private const int Downloadable = 1, Downloading = 2, Available = 3;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Java.Lang.Object? _futures;

    public async Task<OnDeviceAiState> GetStateAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return new OnDeviceAiState(OnDeviceAiStatus.Unsupported, "Needs Android 8 or newer.");
        }

        try
        {
            var status = await Task.Run(() => Wait<Integer>(Call(Futures(), "checkStatus")).IntValue());
            return status switch
            {
                Available => new OnDeviceAiState(OnDeviceAiStatus.Ready),
                Downloadable => new OnDeviceAiState(OnDeviceAiStatus.NeedsDownload),
                Downloading => new OnDeviceAiState(OnDeviceAiStatus.Downloading),
                _ => new OnDeviceAiState(OnDeviceAiStatus.Unsupported, "This phone doesn't have Gemini Nano.")
            };
        }
        catch (Exception e)
        {
            return new OnDeviceAiState(OnDeviceAiStatus.Unsupported, Describe(e));
        }
    }

    public async Task<string?> DownloadAsync(IProgress<long>? progress)
    {
        try
        {
            await Task.Run(() =>
            {
                var futures = Futures();
                var callbackType = Load("com.google.mlkit.genai.common.DownloadCallback");
                var proxy = Proxy.NewProxyInstance(callbackType.ClassLoader, [callbackType], new DownloadHandler(progress));
                Wait(futures.Class.GetMethod("download", callbackType)!.Invoke(futures, proxy)!);
            });
            return null;
        }
        catch (Exception e)
        {
            return Describe(e);
        }
    }

    // The library reports download progress through a Java interface; a dynamic proxy stands in for it.
    private sealed class DownloadHandler(IProgress<long>? progress) : Java.Lang.Object, IInvocationHandler
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? proxy, Method? method, Java.Lang.Object[]? args)
        {
            switch (method?.Name)
            {
                case "onDownloadProgress" when args is { Length: 1 } && args[0] is Java.Lang.Long bytes:
                    progress?.Report(bytes.LongValue());
                    return null;
                case "hashCode":
                    return Integer.ValueOf(GetHashCode());
                case "equals":
                    return Java.Lang.Boolean.ValueOf(ReferenceEquals(proxy, args?[0]));
                case "toString":
                    return new Java.Lang.String("DownloadHandler");
                default:
                    return null;
            }
        }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancel = default)
    {
        // One request at a time: the library answers BUSY if it's hit with several at once.
        await _gate.WaitAsync(cancel);
        try
        {
            return await Task.Run(() =>
            {
                var futures = Futures();
                var method = futures.Class.GetMethod("generateContent", Class.FromType(typeof(Java.Lang.String)));
                var response = Wait<Java.Lang.Object>(method!.Invoke(futures, new Java.Lang.String(prompt)));
                var candidates = response.Class.GetMethod("getCandidates")!.Invoke(response).JavaCast<Java.Util.IList>();
                if (candidates is null || candidates.IsEmpty)
                {
                    throw new OnDeviceAiException("The on-device assistant had no answer.");
                }
                var first = candidates.Get(0)!;
                return first.Class.GetMethod("getText")!.Invoke(first)?.ToString() ?? "";
            }, cancel);
        }
        catch (OnDeviceAiException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new OnDeviceAiException(Describe(e));
        }
        finally
        {
            _gate.Release();
        }
    }

    private Java.Lang.Object Futures()
    {
        if (_futures is not null)
        {
            return _futures;
        }
        var generation = Load(Pkg + "Generation");
        var model = generation.GetMethod("getClient")!.Invoke(generation.GetField("INSTANCE")!.Get(null));
        var futuresClass = Load(Pkg + "java.GenerativeModelFutures");
        _futures = futuresClass.GetMethod("from", Load(Pkg + "GenerativeModel"))!.Invoke(null, model);
        return _futures!;
    }

    // Class.ForName from managed code searches the system loader, which can't see classes packaged inside the app.
    private static Class Load(string name) =>
        Class.ForName(name, true, global::Android.App.Application.Context.ClassLoader);

    private static Java.Lang.Object Call(Java.Lang.Object target, string name) =>
        target.Class.GetMethod(name)!.Invoke(target)!;

    private static Java.Lang.Object? Wait(Java.Lang.Object future) =>
        Class.ForName("java.util.concurrent.Future").GetMethod("get")!.Invoke(future);

    private static T Wait<T>(Java.Lang.Object future) where T : Java.Lang.Object =>
        Wait(future)!.JavaCast<T>();

    private static string Describe(Exception e)
    {
        Throwable? cause = e as Throwable;
        for (var depth = 0; depth < 5; depth++)
        {
            Throwable? next = cause switch
            {
                InvocationTargetException ite => ite.Cause,
                Java.Util.Concurrent.ExecutionException ee => ee.Cause,
                _ => null
            };
            if (next is null)
            {
                break;
            }
            cause = next;
        }
        var text = cause?.Message ?? e.Message;
        return text.Contains("BUSY", StringComparison.OrdinalIgnoreCase)
            ? "The assistant is busy. Try again in a moment."
            : text;
    }
}
