using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _photoPath;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _street = string.Empty;

    [ObservableProperty]
    private string _city = string.Empty;

    [ObservableProperty]
    private string _state = string.Empty;

    [ObservableProperty]
    private string _postalCode = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public ProfileViewModel(IProfileService profileService)
    {
        _profileService = profileService;
        var profile = _profileService.GetProfile();
        _displayName = profile.DisplayName;
        _photoPath = profile.PhotoPath;
        _phoneNumber = profile.PhoneNumber;
        _email = profile.Email;
        _street = profile.Street;
        _city = profile.City;
        _state = profile.State;
        _postalCode = profile.PostalCode;
    }

    [RelayCommand]
    private async Task PickPhoto()
    {
        var path = await _profileService.PickPhotoAsync();
        if (path is not null)
        {
            PhotoPath = path;
        }
    }

    // Only fills the form; nothing is kept until Save.
    [RelayCommand]
    private async Task FillFromPhone()
    {
        var details = await _profileService.ReadOwnerDetailsAsync();
        if (details is null)
        {
            StatusMessage = "Your phone's own contact card has no details to fill in yet.";
            return;
        }

        DisplayName = Pick(details.DisplayName, DisplayName);
        PhoneNumber = Pick(details.PhoneNumber, PhoneNumber);
        Email = Pick(details.Email, Email);
        if (details.Address is { } address)
        {
            FillAddress(address);
        }
        PhotoPath ??= details.PhotoPath;
        StatusMessage = "Filled in from your phone. Check the details, then tap Save.";
    }

    [RelayCommand]
    private async Task UseCurrentLocation()
    {
        var address = await _profileService.LookUpCurrentAddressAsync();
        if (address is null)
        {
            StatusMessage = "Couldn't find an address for where you are.";
            return;
        }

        FillAddress(address);
        StatusMessage = "Address filled in from your location. Check it, then tap Save.";
    }

    [RelayCommand]
    private void Save()
    {
        _profileService.SaveProfile(new UserProfile
        {
            DisplayName = DisplayName,
            PhotoPath = PhotoPath,
            PhoneNumber = PhoneNumber,
            Email = Email,
            Street = Street,
            City = City,
            State = State,
            PostalCode = PostalCode
        });
    }

    private void FillAddress(PostalAddress address)
    {
        Street = Pick(address.Street, Street);
        City = Pick(address.City, City);
        State = Pick(address.State, State);
        PostalCode = Pick(address.PostalCode, PostalCode);
    }

    private static string Pick(string? found, string current) => string.IsNullOrWhiteSpace(found) ? current : found.Trim();
}
