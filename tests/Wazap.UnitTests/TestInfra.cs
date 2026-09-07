using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Wazap.Application.Abstractions;
using Wazap.Infrastructure.Data;

namespace Wazap.UnitTests;

/// <summary>
/// Doubles partagés des tests des services applicatifs (couche API) :
/// contexte EF InMemory, WhatsApp, mot de passe, configuration et environnement.
/// </summary>
internal static class TestInfra
{
    /// <summary>Contexte isolé (base InMemory nommée — unique par test).</summary>
    public static ApplicationDbContext NewContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }
}

/// <summary>Hasher de mots de passe factice (aucun vrai hash).</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hash:{password}";
    public bool Verify(string password, string hashed) => hashed == $"hash:{password}";
}

/// <summary>Enregistre les messages texte/templates au lieu de les envoyer.</summary>
internal sealed class RecordingWhatsAppSender : IWhatsAppSender
{
    public List<(string Phone, string Message)> TextMessages { get; } = new();
    public List<(string Phone, string Template, Dictionary<string, string> Variables)> TemplateMessages { get; } = new();

    public Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables)
    {
        TemplateMessages.Add((toPhoneNumber, templateName, variables));
        return Task.CompletedTask;
    }

    public Task SendTextMessageAsync(string toPhoneNumber, string message)
    {
        TextMessages.Add((toPhoneNumber, message));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Configuration minimale : renvoie la valeur du dictionnaire pour l'indexeur
/// (config["Cle"]) et des sections vides pour le reste.
/// </summary>
internal sealed class ConfigStub : IConfiguration
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public ConfigStub(params (string Key, string? Value)[] values)
    {
        foreach (var (key, value) in values)
            _values[key] = value;
    }

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => [];
    public IConfigurationSection GetSection(string key) => new StubSection(this, key);
    public IChangeToken GetReloadToken() => new StubChangeToken();

    private sealed class StubSection : IConfigurationSection
    {
        private readonly ConfigStub _parent;
        private readonly string _key;

        public StubSection(ConfigStub parent, string key)
        {
            _parent = parent;
            _key = key;
        }

        public string Key => _key;
        public string Path => _key;
        public string? Value
        {
            get => _parent[_key];
            set => _parent[_key] = value;
        }
        public string? this[string childKey]
        {
            get => _parent[$"{_key}:{childKey}"];
            set => _parent[$"{_key}:{childKey}"] = value;
        }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IConfigurationSection GetSection(string childKey) => new StubSection(_parent, $"{_key}:{childKey}");
        public IChangeToken GetReloadToken() => new StubChangeToken();
    }

    private sealed class StubChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>Environnement d'hébergement factice pointant vers un dossier temporaire.</summary>
internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
    }

    public string ApplicationName { get; set; } = "Wazap.Tests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
