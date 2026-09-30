using Microsoft.Extensions.Logging;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;
using ForgeLinkSms.Platforms.Android;
using ForgeLinkSms.Services;

namespace ForgeLinkSms;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		builder.Services.AddSingleton<IDefaultAppRoleService, DefaultAppRoleService>();
		builder.Services.AddSingleton<IBootDetectionService, BootDetectionService>();
		builder.Services.AddSingleton<IPermissionService, PermissionService>();
		builder.Services.AddSingleton<IContactService, ContactService>();
		builder.Services.AddSingleton<IThreadService, ThreadService>();
		builder.Services.AddSingleton<ISmsService, SmsService>();
		builder.Services.AddSingleton<ICalendarService, CalendarService>();
		builder.Services.AddSingleton<ILocationService, LocationService>();
		builder.Services.AddSingleton<IAttachmentPickerService, AttachmentPickerService>();
		builder.Services.AddSingleton<IVoiceRecorderService, VoiceRecorderService>();
		builder.Services.AddSingleton<ForwardRequestStore>();
		builder.Services.AddSingleton<IMediaThumbnailService, MediaThumbnailService>();
		builder.Services.AddSingleton(new LinkPreviewService(new HttpClient()));
		builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
		builder.Services.AddSingleton<IAttachmentSaveService, AttachmentSaveService>();
		builder.Services.AddSingleton<IThreadDeletionService, ThreadDeletionService>();
		builder.Services.AddScoped<INavigationService, NavigationService>();
		builder.Services.AddTransient<SplashViewModel>();
		builder.Services.AddTransient<OnboardingViewModel>();
		builder.Services.AddTransient<ConversationsViewModel>();
		builder.Services.AddTransient<ComposeViewModel>();
		builder.Services.AddSingleton<INotificationService, NotificationService>();

		var trashRepository = new TrashRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		trashRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<ITrashRepository>(trashRepository);
		builder.Services.AddSingleton<IUndoStack, UndoStack>();
		builder.Services.AddTransient<TrashViewModel>();

		var blockedNumberRepository = new BlockedNumberRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		blockedNumberRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IBlockedNumberRepository>(blockedNumberRepository);
		builder.Services.AddSingleton<IContactBlockService, ContactBlockService>();
		builder.Services.AddTransient<BlockedViewModel>();

		var favoriteRepository = new FavoriteRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		favoriteRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IFavoriteRepository>(favoriteRepository);

		var archiveRepository = new ArchiveRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		archiveRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IArchiveRepository>(archiveRepository);
		builder.Services.AddTransient<ArchivedViewModel>();

		var muteRepository = new MuteRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		muteRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IMuteRepository>(muteRepository);

		var draftRepository = new DraftRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		draftRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IDraftRepository>(draftRepository);

		var forwardedMessageRepository = new ForwardedMessageRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		forwardedMessageRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IForwardedMessageRepository>(forwardedMessageRepository);

		var quickReplyRepository = new QuickReplyRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		quickReplyRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IQuickReplyRepository>(quickReplyRepository);
		builder.Services.AddTransient<QuickRepliesViewModel>();

		var filterRepository = new FilterRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		filterRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IFilterRepository>(filterRepository);
		builder.Services.AddTransient<FiltersViewModel>();

		var scheduledMessageRepository = new ScheduledMessageRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		scheduledMessageRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IScheduledMessageRepository>(scheduledMessageRepository);
		builder.Services.AddSingleton<IMessageSchedulerService, MessageSchedulerService>();
		builder.Services.AddTransient<ScheduledViewModel>();

		var snoozeRepository = new SnoozeRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		snoozeRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<ISnoozeRepository>(snoozeRepository);
		builder.Services.AddSingleton<ISnoozeAlarmScheduler, SnoozeAlarmScheduler>();
		builder.Services.AddSingleton<ISnoozeService, SnoozeService>();
		builder.Services.AddTransient<SnoozedViewModel>();

		var reminderRepository = new ReminderRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		reminderRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IReminderRepository>(reminderRepository);
		builder.Services.AddSingleton<IReminderAlarmScheduler, ReminderAlarmScheduler>();
		builder.Services.AddSingleton<IReminderService, ReminderService>();
		builder.Services.AddTransient<RemindersViewModel>();

		var allowedSenderRepository = new AllowedSenderRepository(Path.Combine(FileSystem.AppDataDirectory, "ForgeLinkSms.db"));
		allowedSenderRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IAllowedSenderRepository>(allowedSenderRepository);

		builder.Services.AddSingleton<IMarkAsReadService, MarkAsReadService>();
		builder.Services.AddTransient<MenuViewModel>();

		builder.Services.AddSingleton<IThemeService, ThemeService>();
		builder.Services.AddSingleton<IDisplayStyleService, DisplayStyleService>();
		builder.Services.AddSingleton<INotificationSettingsStore, NotificationSettingsStore>();
		builder.Services.AddSingleton<IReviewPromptService, ReviewPromptService>();
		builder.Services.AddSingleton<IBackupService, ForgeLinkSms.Platforms.Android.Backup.BackupService>();
		builder.Services.AddTransient<ThemeViewModel>();

		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddSingleton<PendingNavigationStore>();
		builder.Services.AddSingleton<NavigationHistoryTracker>();

		builder.Services.AddSingleton<IProfileService, ProfileService>();
		builder.Services.AddTransient<ProfileViewModel>();

		builder.Services.AddSingleton<IAppResumeNotifier, AppResumeNotifier>();
		builder.Services.AddSingleton<IIncomingMessageNotifier, IncomingMessageNotifier>();

		builder.Logging.AddProvider(new ErrorDetailsLoggerProvider());
#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
