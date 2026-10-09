namespace GhostLetters.Infrastructure.Persistence.Entities;

/// <summary>Набор карт: встроенный или свой (позже).</summary>
public sealed class CardSet
{
    public Guid Id { get; set; }

    /// <summary>original / mailbox / ritual / mirror или код своего набора.</summary>
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool IsBuiltin { get; set; }

    public Guid? OwnerId { get; set; }
}

/// <summary>Карта улики. ImageKey — имя файла из манифеста нарезки (tools/cards).</summary>
public sealed class Card
{
    public Guid Id { get; set; }

    public Guid SetId { get; set; }

    public string ImageKey { get; set; } = string.Empty;

    public string? Title { get; set; }

    public bool IsActive { get; set; } = true;

    public bool SetManuallyAssigned { get; set; }

    public string? Annotations { get; set; }

    public int MetadataVersion { get; set; }
}

/// <summary>Номинация ачивки из каталога.</summary>
public sealed class Nomination
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
