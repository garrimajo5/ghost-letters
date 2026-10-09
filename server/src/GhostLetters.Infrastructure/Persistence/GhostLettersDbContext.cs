using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GhostLetters.Infrastructure.Persistence;

/// <summary>Контекст БД. Имена таблиц и колонок — snake_case, JSON — jsonb, время — timestamptz.</summary>
public sealed class GhostLettersDbContext(DbContextOptions<GhostLettersDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<SettingsPreset> SettingsPresets => Set<SettingsPreset>();

    public DbSet<AuthIdentity> AuthIdentities => Set<AuthIdentity>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PushToken> PushTokens => Set<PushToken>();

    public DbSet<CardSet> CardSets => Set<CardSet>();

    public DbSet<Card> Cards => Set<Card>();

    public DbSet<Nomination> Nominations => Set<Nomination>();

    public DbSet<Lobby> Lobbies => Set<Lobby>();

    public DbSet<LobbyMember> LobbyMembers => Set<LobbyMember>();

    public DbSet<Game> Games => Set<Game>();

    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();

    public DbSet<GameEventRecord> GameEvents => Set<GameEventRecord>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<Media> MediaFiles => Set<Media>();

    public DbSet<Vote> Votes => Set<Vote>();

    public DbSet<PlayerNote> PlayerNotes => Set<PlayerNote>();

    public DbSet<CardMark> CardMarks => Set<CardMark>();

    public DbSet<Like> Likes => Set<Like>();

    public DbSet<AwardNomination> AwardNominations => Set<AwardNomination>();

    public DbSet<AwardVote> AwardVotes => Set<AwardVote>();

    public DbSet<UserAchievement> UserAchievements => Set<UserAchievement>();

    public DbSet<UserStats> Stats => Set<UserStats>();

    public DbSet<RatingHistory> RatingHistoryRecords => Set<RatingHistory>();

    public DbSet<BotRelationship> BotRelationships => Set<BotRelationship>();
    public DbSet<BotRelationshipGame> BotRelationshipGames => Set<BotRelationshipGame>();

    public DbSet<BotProfile> BotProfiles => Set<BotProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        ConfigureAccounts(modelBuilder);
        ConfigureCatalog(modelBuilder);
        ConfigureLobbies(modelBuilder);
        ConfigureGames(modelBuilder);
        ConfigurePersonal(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureAccounts(ModelBuilder b)
    {
        b.Entity<SettingsPreset>(e =>
        {
            e.ToTable("settings_presets");
            e.Property(x => x.Name).HasMaxLength(40);
            e.Property(x => x.Settings).HasColumnType("jsonb");
            e.HasIndex(x => x.UserId);
            UserFk(e, x => x.UserId);
        });
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Nickname).HasMaxLength(User.MaxNicknameLength);
            e.Property(x => x.AvatarColor).HasMaxLength(7);
            e.Property(x => x.IsBot).HasDefaultValue(false);
        });

        b.Entity<AuthIdentity>(e =>
        {
            e.ToTable("auth_identities");
            e.Property(x => x.Provider).HasMaxLength(16);
            e.Property(x => x.Subject).HasMaxLength(256);
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            e.HasIndex(x => x.UserId);
            UserFk(e, x => x.UserId);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            UserFk(e, x => x.UserId);
        });

        b.Entity<PushToken>(e =>
        {
            e.ToTable("push_tokens");
            e.Property(x => x.Platform).HasMaxLength(16);
            e.Property(x => x.Token).HasMaxLength(512);
            e.HasIndex(x => new { x.UserId, x.Token }).IsUnique();
            UserFk(e, x => x.UserId);
        });
    }

    private static void ConfigureCatalog(ModelBuilder b)
    {
        b.Entity<CardSet>(e =>
        {
            e.ToTable("card_sets");
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(64);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
            e.HasData(CatalogSeed.CardSets);
        });

        b.Entity<Card>(e =>
        {
            e.ToTable("cards");
            e.Property(x => x.ImageKey).HasMaxLength(128);
            e.Property(x => x.Title).HasMaxLength(64);
            e.HasIndex(x => new { x.SetId, x.ImageKey }).IsUnique();
            e.HasOne<CardSet>().WithMany().HasForeignKey(x => x.SetId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Nomination>(e =>
        {
            e.ToTable("nominations");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(256);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasData(CatalogSeed.Nominations);
        });
    }

    private static void ConfigureLobbies(ModelBuilder b)
    {
        b.Entity<Lobby>(e =>
        {
            e.ToTable("lobbies");
            e.Property(x => x.Code).HasMaxLength(Lobby.CodeLength);
            e.Property(x => x.Title).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Settings).HasColumnType("jsonb");

            // Код уникален только среди незакрытых лобби — закрытые коды можно переиспользовать.
            e.HasIndex(x => x.Code).IsUnique().HasFilter("status <> 'closed'");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LobbyMember>(e =>
        {
            e.ToTable("lobby_members");
            e.HasKey(x => new { x.LobbyId, x.UserId });
            e.Property(x => x.JoinMode).HasMaxLength(16);
            e.HasIndex(x => x.UserId);
            e.HasOne<Lobby>().WithMany().HasForeignKey(x => x.LobbyId).OnDelete(DeleteBehavior.Cascade);
            UserFk(e, x => x.UserId);
        });
    }

    private static void ConfigureGames(ModelBuilder b)
    {
        b.Entity<Game>(e =>
        {
            e.ToTable("games");
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Phase).HasMaxLength(32);
            e.Property(x => x.Settings).HasColumnType("jsonb");
            e.Property(x => x.State).HasColumnType("jsonb");
            e.Property(x => x.Result).HasColumnType("jsonb");
            e.Property(x => x.Version).IsConcurrencyToken();

            // Для фоновой службы таймеров.
            e.HasIndex(x => new { x.Status, x.PhaseDeadline });
            e.HasOne<Lobby>().WithMany().HasForeignKey(x => x.LobbyId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<GamePlayer>(e =>
        {
            e.ToTable("game_players");
            e.HasKey(x => new { x.GameId, x.UserId });
            e.Property(x => x.Role).HasMaxLength(16);
            e.Property(x => x.CharacterCode).HasMaxLength(32);
            e.HasIndex(x => x.UserId);
            GameFk(e, x => x.GameId);
            UserFk(e, x => x.UserId, DeleteBehavior.Restrict);
        });

        b.Entity<GameEventRecord>(e =>
        {
            e.ToTable("game_events");
            e.HasKey(x => new { x.GameId, x.Seq });
            e.Property(x => x.Type).HasMaxLength(32);
            e.Property(x => x.Visibility).HasMaxLength(8);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.ClientCommandId).HasMaxLength(64);
            e.HasIndex(x => new { x.GameId, x.ActorUserId, x.ClientCommandId })
                .HasFilter("client_command_id IS NOT NULL");
            GameFk(e, x => x.GameId);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_messages");
            e.Property(x => x.Channel).HasMaxLength(16);
            e.Property(x => x.Kind).HasMaxLength(8);
            e.Property(x => x.Text).HasMaxLength(1000);
            e.HasIndex(x => new { x.GameId, x.CreatedAt });
            GameFk(e, x => x.GameId);
            e.HasOne<Media>().WithMany().HasForeignKey(x => x.MediaId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Media>(e =>
        {
            e.ToTable("media");
            e.Property(x => x.ContentType).HasMaxLength(64);
            e.Property(x => x.StorageKey).HasMaxLength(256);
            UserFk(e, x => x.OwnerId);
        });

        b.Entity<Vote>(e =>
        {
            e.ToTable("votes");
            e.Property(x => x.StageKind).HasMaxLength(8);
            e.HasIndex(x => new { x.GameId, x.Stage, x.Attempt, x.VoterId }).IsUnique();
            GameFk(e, x => x.GameId);
        });
    }

    private static void ConfigurePersonal(ModelBuilder b)
    {
        b.Entity<PlayerNote>(e =>
        {
            e.ToTable("player_notes");
            e.HasKey(x => new { x.GameId, x.OwnerId, x.TargetUserId });
            e.Property(x => x.Body).HasMaxLength(2000);
            e.Property(x => x.Entries).HasColumnType("jsonb");
            GameFk(e, x => x.GameId);
        });

        b.Entity<CardMark>(e =>
        {
            e.ToTable("card_marks");
            e.HasKey(x => new { x.GameId, x.OwnerId, x.CardId });
            e.Property(x => x.CardId).HasMaxLength(128);
            e.Property(x => x.Sources).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            GameFk(e, x => x.GameId);
        });

        b.Entity<Like>(e =>
        {
            e.ToTable("likes");
            e.HasKey(x => new { x.GameId, x.FromUserId, x.ToUserId });
            e.HasIndex(x => x.ToUserId);
            GameFk(e, x => x.GameId);
        });

        b.Entity<AwardNomination>(e =>
        {
            e.ToTable("award_nominations");
            e.Property(x => x.NominationCode).HasMaxLength(40);
            e.HasIndex(x => new { x.GameId, x.NominatorId }).IsUnique();
            GameFk(e, x => x.GameId);
        });

        b.Entity<AwardVote>(e =>
        {
            e.ToTable("award_votes");
            e.HasKey(x => new { x.GameId, x.VoterId });
            e.HasOne<AwardNomination>().WithMany().HasForeignKey(x => x.AwardNominationId).OnDelete(DeleteBehavior.Cascade);
            GameFk(e, x => x.GameId, DeleteBehavior.NoAction);
        });

        b.Entity<UserAchievement>(e =>
        {
            e.ToTable("user_achievements");
            e.Property(x => x.NominationCode).HasMaxLength(40);
            e.HasIndex(x => x.UserId);
            UserFk(e, x => x.UserId);
        });

        b.Entity<UserStats>(e =>
        {
            e.ToTable("user_stats");
            e.HasKey(x => x.UserId);
            e.Property(x => x.RoleWins).HasColumnType("jsonb");
            e.HasIndex(x => x.Rating);
            UserFk(e, x => x.UserId);
        });

        b.Entity<RatingHistory>(e =>
        {
            e.ToTable("rating_history");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            UserFk(e, x => x.UserId);
        });

        b.Entity<BotRelationship>(e =>
        {
            e.ToTable("bot_relationships");
            e.HasKey(x => new { x.BotId, x.PlayerId });
            e.Property(x => x.Components).HasColumnType("jsonb");
            UserFk(e, x => x.BotId);
            UserFk(e, x => x.PlayerId);
        });
        b.Entity<BotRelationshipGame>(e =>
        {
            e.ToTable("bot_relationship_games");
            e.HasKey(x => new { x.GameId, x.BotId });
            GameFk(e, x => x.GameId);
            UserFk(e, x => x.BotId);
        });
        b.Entity<BotProfile>(e =>
        {
            e.ToTable("bot_profiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.About).HasMaxLength(300);
            UserFk(e, x => x.UserId);
        });
    }

    private static void UserFk<T>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, object?>> key,
        DeleteBehavior onDelete = DeleteBehavior.Cascade)
        where T : class =>
        e.HasOne<User>().WithMany().HasForeignKey(key).OnDelete(onDelete);

    private static void GameFk<T>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, object?>> key,
        DeleteBehavior onDelete = DeleteBehavior.Cascade)
        where T : class =>
        e.HasOne<Game>().WithMany().HasForeignKey(key).OnDelete(onDelete);
}
