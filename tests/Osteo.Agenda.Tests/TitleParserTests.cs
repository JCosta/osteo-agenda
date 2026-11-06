using Osteo.Agenda.Core.Contacts;
using Osteo.Agenda.Core.Domain;
using Osteo.Agenda.Core.Import;

namespace Osteo.Agenda.Tests;

public class TitleParserTests
{
    [Theory]
    [InlineData("Maria Silva #912345678", "Maria Silva", "+351912345678")]
    [InlineData("Maria Silva #912 345 678", "Maria Silva", "+351912345678")]
    [InlineData("  Maria   Silva  # +351 912345678 ", "Maria Silva", "+351912345678")]
    [InlineData("Tomás #00351912345678", "Tomás", "+351912345678")]
    [InlineData("John #+44 7700 900123", "John", "+447700900123")]
    public void Reads_name_and_phone(string title, string name, string e164)
    {
        var p = TitleParser.Parse(title);
        Assert.Equal(name, p.Name);
        Assert.Equal(e164, p.Phone?.E164);
        Assert.False(p.ConfirmedMark);
        Assert.False(p.IsBlock);
    }

    [Theory]
    [InlineData("✅ Maria #912345678")]
    [InlineData("✅Maria #912345678")]
    [InlineData("✔️ Maria #912345678")]
    public void Detects_confirmed_prefix(string title)
    {
        var p = TitleParser.Parse(title);
        Assert.True(p.ConfirmedMark);
        Assert.Equal("Maria", p.Name);
    }

    [Fact]
    public void Reads_service_and_channel_tags_in_any_order()
    {
        var p = TitleParser.Parse("Ana Costa #sms #Bebé 1.ª consulta #932111222");
        Assert.Equal("Ana Costa", p.Name);
        Assert.Equal("+351932111222", p.Phone?.E164);
        Assert.Equal(Channel.Sms, p.Channel);
        Assert.Equal("Bebé 1.ª consulta", p.ServiceHint);
    }

    [Theory]
    [InlineData("#bloqueio")]
    [InlineData("#bloqueio Dentista")]
    [InlineData("Férias #BLOQUEIO")]
    public void Detects_blocks(string title) => Assert.True(TitleParser.Parse(title).IsBlock);

    [Fact]
    public void Missing_phone_is_null()
    {
        var p = TitleParser.Parse("Reunião com contabilista");
        Assert.Null(p.RawPhone);
        Assert.Null(p.Phone);
    }

    [Fact]
    public void Too_short_number_is_raw_but_invalid()
    {
        var p = TitleParser.Parse("Rui #91234567");
        Assert.Equal("91234567", p.RawPhone);
        Assert.Null(p.Phone);
    }

    [Theory]
    [InlineData("912345678", PhoneType.Mobile)]
    [InlineData("253123456", PhoneType.Landline)]
    [InlineData("+351 253 123 456", PhoneType.Landline)]
    public void Classifies_portuguese_numbers(string raw, PhoneType type) =>
        Assert.Equal(type, PhoneNormalizer.Normalize(raw)?.Type);

    [Fact]
    public void Display_formats_portuguese_numbers() =>
        Assert.Equal("912 345 678", PhoneNormalizer.Display("+351912345678"));
}
