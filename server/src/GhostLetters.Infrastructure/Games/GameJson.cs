using System.Text.Json;
using System.Text.Json.Serialization;
using GhostLetters.Application;
using GhostLetters.Domain.Game;

namespace GhostLetters.Infrastructure.Games;

/// <summary>JSON для снимков состояния, настроек и команд: camelCase, перечисления строками.</summary>
public static class GameJson
{
    public static readonly JsonSerializerOptions Options = Create();

    /// <summary>Команды, которые клиент может прислать, по имени типа.</summary>
    private static readonly Dictionary<string, Type> Commands = typeof(GameCommand).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(GameCommand)) && !t.IsAbstract)
        .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException($"Пустой JSON для {typeof(T).Name}.");

    /// <summary>Команда из {type, payload}. Неизвестный тип или кривые поля — VALIDATION.</summary>
    public static GameCommand ParseCommand(string type, JsonElement? payload)
    {
        if (string.IsNullOrWhiteSpace(type) || !Commands.TryGetValue(type, out var commandType))
        {
            throw AppException.Validation($"Неизвестная команда «{type}».");
        }

        try
        {
            var json = payload is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } p ? p.GetRawText() : "{}";
            return (GameCommand?)JsonSerializer.Deserialize(json, commandType, Options)
                   ?? throw AppException.Validation("Пустая команда.");
        }
        catch (JsonException e)
        {
            throw AppException.Validation($"Неверные поля команды {commandType.Name}: {e.Message}");
        }
    }
}
