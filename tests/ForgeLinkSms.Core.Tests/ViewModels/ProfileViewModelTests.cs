using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ProfileViewModelTests
{
    [Fact]
    public void Constructor_loads_the_currently_saved_profile()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "Travis", PhotoPath = "/path/photo.jpg" });

        var viewModel = new ProfileViewModel(profileService.Object);

        Assert.Equal("Travis", viewModel.DisplayName);
        Assert.Equal("/path/photo.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public async Task PickPhotoCommand_updates_PhotoPath_when_a_photo_is_picked()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = null });
        profileService.Setup(s => s.PickPhotoAsync()).ReturnsAsync("/path/new.jpg");
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal("/path/new.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public async Task PickPhotoCommand_leaves_PhotoPath_unchanged_when_cancelled()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = "/path/old.jpg" });
        profileService.Setup(s => s.PickPhotoAsync()).ReturnsAsync((string?)null);
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.PickPhotoCommand.ExecuteAsync(null);

        Assert.Equal("/path/old.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public void SaveCommand_saves_every_field()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "", PhotoPath = null });
        var viewModel = new ProfileViewModel(profileService.Object)
        {
            DisplayName = "Travis", PhotoPath = "/path/photo.jpg", PhoneNumber = "4045550199", Email = "t@example.com",
            Street = "12 Oak St", City = "Atlanta", State = "GA", PostalCode = "30301"
        };

        viewModel.SaveCommand.Execute(null);

        profileService.Verify(s => s.SaveProfile(It.Is<UserProfile>(p =>
            p.DisplayName == "Travis" && p.PhotoPath == "/path/photo.jpg" && p.PhoneNumber == "4045550199" && p.Email == "t@example.com"
            && p.Street == "12 Oak St" && p.City == "Atlanta" && p.State == "GA" && p.PostalCode == "30301")), Times.Once);
    }

    [Fact]
    public async Task Filling_from_the_phone_replaces_fields_it_found_and_keeps_the_rest()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "Trav", Email = "old@example.com", PhotoPath = "/mine.jpg" });
        profileService.Setup(s => s.ReadOwnerDetailsAsync()).ReturnsAsync(new ProfileDetails(
            "Travis Donnelly", "4045550199", null, new PostalAddress("12 Oak St", "Atlanta", "GA", "30301"), "/owner.jpg"));
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.FillFromPhoneCommand.ExecuteAsync(null);

        Assert.Equal("Travis Donnelly", viewModel.DisplayName);
        Assert.Equal("4045550199", viewModel.PhoneNumber);
        Assert.Equal("old@example.com", viewModel.Email);
        Assert.Equal("Atlanta", viewModel.City);
        Assert.Equal("/mine.jpg", viewModel.PhotoPath);
        profileService.Verify(s => s.SaveProfile(It.IsAny<UserProfile>()), Times.Never);
    }

    [Fact]
    public async Task Filling_from_the_phone_uses_its_photo_only_when_there_is_none()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "" });
        profileService.Setup(s => s.ReadOwnerDetailsAsync()).ReturnsAsync(new ProfileDetails(null, null, null, null, "/owner.jpg"));
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.FillFromPhoneCommand.ExecuteAsync(null);

        Assert.Equal("/owner.jpg", viewModel.PhotoPath);
    }

    [Fact]
    public async Task Says_so_when_the_phone_has_no_owner_card()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "Travis" });
        profileService.Setup(s => s.ReadOwnerDetailsAsync()).ReturnsAsync((ProfileDetails?)null);
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.FillFromPhoneCommand.ExecuteAsync(null);

        Assert.Equal("Travis", viewModel.DisplayName);
        Assert.False(string.IsNullOrEmpty(viewModel.StatusMessage));
    }

    [Fact]
    public async Task Current_location_fills_only_the_address()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "Travis", Street = "Old St" });
        profileService.Setup(s => s.LookUpCurrentAddressAsync()).ReturnsAsync(new PostalAddress("12 Oak St", "Atlanta", "GA", "30301"));
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.UseCurrentLocationCommand.ExecuteAsync(null);

        Assert.Equal("12 Oak St", viewModel.Street);
        Assert.Equal("30301", viewModel.PostalCode);
        Assert.Equal("Travis", viewModel.DisplayName);
    }

    [Fact]
    public async Task Says_so_when_the_current_address_cannot_be_found()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(s => s.GetProfile()).Returns(new UserProfile { DisplayName = "Travis", Street = "Old St" });
        profileService.Setup(s => s.LookUpCurrentAddressAsync()).ReturnsAsync((PostalAddress?)null);
        var viewModel = new ProfileViewModel(profileService.Object);

        await viewModel.UseCurrentLocationCommand.ExecuteAsync(null);

        Assert.Equal("Old St", viewModel.Street);
        Assert.False(string.IsNullOrEmpty(viewModel.StatusMessage));
    }
}
