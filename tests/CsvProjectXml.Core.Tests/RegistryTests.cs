using System.Text;
using CsvProjectXml.Core.Models;
using CsvProjectXml.Core.Services;
using Xunit;

namespace CsvProjectXml.Core.Tests;

public sealed class RegistryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CsvProjectXmlRegistry_").FullName;
    private readonly NamesRegistryService _registry = new();

    [Fact]
    public async Task ReadsUtf8BomAndRetainsStoredSlugInsteadOfReDerivingIt()
    {
        var path = Path.Combine(_root, "names.json");
        var json = "[ { \"index\": 13, \"value\": \"_hyeh_altnmya_alsnaaya\", \"text\": \"  المطور الصناعي (العقود)  \" } ]";
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var entries = await _registry.LoadAsync(path);

        Assert.Single(entries);
        Assert.Equal("_hyeh_altnmya_alsnaaya", entries[0].Value);
        Assert.Equal("المطور الصناعي (العقود)", entries[0].DisplayName);
    }

    [Fact]
    public void NewDepartmentSlugUsesTheSpecifiedTransliterationAndSeparatorRule()
    {
        var created = _registry.Create("قسم - ب", Array.Empty<DepartmentEntry>());

        Assert.Equal(1, created.Index);
        Assert.Equal("qsm___b_hyeh_altnmya_alsnaaya", created.Value);
        Assert.Equal("قسم - ب", created.Text);
    }

    [Fact]
    public async Task SavingRegistryKeepsTheOriginalSlugUnlessTheOwnerChangesIt()
    {
        var path = Path.Combine(_root, "names.json");
        var entry = new DepartmentEntry
        {
            Index = 56,
            Value = "khtbat_hyeh_altnmya_alsnaaya",
            Text = "الإدارة القديمة"
        };
        await _registry.SaveAsync(path, new[] { entry });
        var loaded = await _registry.LoadAsync(path);

        Assert.Equal("khtbat_hyeh_altnmya_alsnaaya", Assert.Single(loaded).Value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
