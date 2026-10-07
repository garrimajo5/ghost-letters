using GhostLetters.Infrastructure.Persistence.Entities;

namespace GhostLetters.Infrastructure.Persistence;

/// <summary>Встроенные наборы карт и номинации. Id фиксированы — данные попадают в миграции.</summary>
public static class CatalogSeed
{
    public static readonly Guid OriginalSetId = new("6f1d0c2e-0001-4a11-9a00-000000000001");

    public static readonly CardSet[] CardSets =
    [
        new() { Id = OriginalSetId, Code = "original", Title = "Оригинальный", IsBuiltin = true },
        new() { Id = new Guid("6f1d0c2e-0001-4a11-9a00-000000000002"), Code = "mailbox", Title = "Почтовый ящик", IsBuiltin = true },
        new() { Id = new Guid("6f1d0c2e-0001-4a11-9a00-000000000003"), Code = "ritual", Title = "Тайный ритуал", IsBuiltin = true },
        new() { Id = new Guid("6f1d0c2e-0001-4a11-9a00-000000000004"), Code = "mirror", Title = "Зеркало истины", IsBuiltin = true },
    ];

    public static readonly Nomination[] Nominations =
    [
        new()
        {
            Id = new Guid("6f1d0c2e-0002-4a11-9a00-000000000001"),
            Code = "steel_balls",
            Title = "Стальные яйца",
            Description = "За самый дерзкий ход партии.",
        },
    ];
}
