using System.Text.RegularExpressions;
using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence.Entities;

namespace GhostLetters.Infrastructure.Auth;

/// <summary>Проверки ника, цвета и deviceId.</summary>
public static partial class ProfileRules
{
    public const int MinDeviceIdLength = 8;
    public const int MaxDeviceIdLength = 128;

    /// <summary>Палитра заглушек-аватаров по умолчанию (приглушённые цвета из дизайна).</summary>
    public static readonly string[] Palette = ["#3E7C6E", "#6A5A9E", "#8A5A44", "#3D6A99", "#7A6A3A", "#9A4F6E", "#4F7F3F", "#5A6E82"];

    public static string Nickname(string? value)
    {
        var nick = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        if (nick.Length is < User.MinNicknameLength or > User.MaxNicknameLength)
        {
            throw AppException.Validation($"Ник — от {User.MinNicknameLength} до {User.MaxNicknameLength} символов.");
        }

        if (nick.Any(char.IsControl))
        {
            throw AppException.Validation("В нике есть недопустимые символы.");
        }

        return nick;
    }

    public static string AvatarColor(string? value)
    {
        if (value is null || !ColorRegex().IsMatch(value))
        {
            throw AppException.Validation("Цвет аватара — в формате #RRGGBB.");
        }

        return value.ToUpperInvariant();
    }

    public static string DeviceId(string? value)
    {
        var id = value?.Trim() ?? string.Empty;
        if (id.Length is < MinDeviceIdLength or > MaxDeviceIdLength)
        {
            throw AppException.Validation($"deviceId — от {MinDeviceIdLength} до {MaxDeviceIdLength} символов.");
        }

        return id;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorRegex();
}
