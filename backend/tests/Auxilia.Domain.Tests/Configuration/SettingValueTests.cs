using Auxilia.Domain.Configuration;

namespace Auxilia.Domain.Tests.Configuration;

public sealed class SettingValueTests
{
    [Fact]
    public void TenantSetting_StoresAndChangesTheJsonValue()
    {
        var setting = new TenantSetting(Guid.CreateVersion7(), "cases.expiry.expiringDays", "7");

        setting.SetValue("30");

        setting.Key.ShouldBe("cases.expiry.expiringDays");
        setting.JsonValue.ShouldBe("30");
    }

    [Fact]
    public void TenantSetting_RejectsMissingOrTooLongValues()
    {
        Should.Throw<ArgumentException>(() => new TenantSetting(Guid.CreateVersion7(), " ", "7"));
        Should.Throw<ArgumentOutOfRangeException>(() => new TenantSetting(Guid.CreateVersion7(), new string('a', TenantSetting.KeyMaxLength + 1), "7"));
        Should.Throw<ArgumentException>(() => new TenantSetting(Guid.CreateVersion7(), "a.b", ""));
        Should.Throw<ArgumentException>(() => new TenantSetting(Guid.CreateVersion7(), "a.b", "1").SetValue(" "));
    }

    [Fact]
    public void UserSetting_BelongsToOneUser()
    {
        var userId = Guid.CreateVersion7();
        var setting = new UserSetting(Guid.CreateVersion7(), userId, "ui.theme", "\"light\"");

        setting.SetValue("\"dark\"");

        setting.UserId.ShouldBe(userId);
        setting.Key.ShouldBe("ui.theme");
        setting.JsonValue.ShouldBe("\"dark\"");
    }

    [Fact]
    public void UserSetting_RejectsInvalidValues()
    {
        Should.Throw<ArgumentException>(() => new UserSetting(Guid.CreateVersion7(), Guid.Empty, "ui.theme", "1"));
        Should.Throw<ArgumentException>(() => new UserSetting(Guid.CreateVersion7(), Guid.CreateVersion7(), "", "1"));
        Should.Throw<ArgumentOutOfRangeException>(() => new UserSetting(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 151), "1"));
        Should.Throw<ArgumentException>(() => new UserSetting(Guid.CreateVersion7(), Guid.CreateVersion7(), "ui.theme", " "));
        Should.Throw<ArgumentException>(() => new UserSetting(Guid.CreateVersion7(), Guid.CreateVersion7(), "ui.theme", "1").SetValue(""));
    }
}
