using LanForge.Server.Application;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Server;

/// <summary>Validaciones de la solicitud de creación de un paquete (POST /api/v1/packages).</summary>
public sealed class CreatePackageValidatorTests
{
    private static CreatePackageRequest Valid() => new()
    {
        GameId = "quake3",
        Title = "Quake III Arena",
        Version = "1.32",
        SourceDirectory = "/srv/games/quake3",
        EntryPoint = "run.exe",
    };

    private static CreatePackageRequest Without(CreatePackageRequest request, string field) => field switch
    {
        "gameId" => request with { GameId = null },
        "title" => request with { Title = null },
        "version" => request with { Version = null },
        "sourceDirectory" => request with { SourceDirectory = null },
        "entryPoint" => request with { EntryPoint = null },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Campo desconocido."),
    };

    [Fact]
    public void Validate_Una_solicitud_valida_no_devuelve_errores()
    {
        Assert.Empty(CreatePackageValidator.Validate(Valid()));
    }

    [Fact]
    public void Validate_Una_solicitud_nula_devuelve_el_error_del_cuerpo()
    {
        var error = Assert.Single(CreatePackageValidator.Validate(null));

        Assert.Equal("body", error.Field);
        Assert.Equal("required", error.Code);
    }

    [Theory]
    [InlineData("gameId")]
    [InlineData("title")]
    [InlineData("version")]
    [InlineData("sourceDirectory")]
    [InlineData("entryPoint")]
    public void Validate_Campo_obligatorio_ausente_devuelve_required(string field)
    {
        var errors = CreatePackageValidator.Validate(Without(Valid(), field));

        var error = Assert.Single(errors);
        Assert.Equal(field, error.Field);
        Assert.Equal("required", error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Campo_obligatorio_en_blanco_devuelve_required(string value)
    {
        var errors = CreatePackageValidator.Validate(Valid() with { GameId = value });

        var error = Assert.Single(errors);
        Assert.Equal("gameId", error.Field);
        Assert.Equal("required", error.Code);
    }

    [Theory]
    [InlineData("quake 3")]
    [InlineData("quake/3")]
    [InlineData("quake.3")]
    public void Validate_GameId_con_caracteres_no_permitidos_devuelve_invalid_format(string gameId)
    {
        var errors = CreatePackageValidator.Validate(Valid() with { GameId = gameId });

        var error = Assert.Single(errors);
        Assert.Equal("gameId", error.Field);
        Assert.Equal("invalid_format", error.Code);
    }

    [Fact]
    public void Validate_GameId_demasiado_largo_devuelve_too_long()
    {
        var errors = CreatePackageValidator.Validate(Valid() with { GameId = new string('a', 65) });

        var error = Assert.Single(errors);
        Assert.Equal("gameId", error.Field);
        Assert.Equal("too_long", error.Code);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("latest")]
    public void Validate_Version_invalida_devuelve_invalid_format(string version)
    {
        var errors = CreatePackageValidator.Validate(Valid() with { Version = version });

        var error = Assert.Single(errors);
        Assert.Equal("version", error.Field);
        Assert.Equal("invalid_format", error.Code);
    }

    [Fact]
    public void Validate_Version_demasiado_larga_devuelve_too_long()
    {
        var errors = CreatePackageValidator.Validate(Valid() with { Version = new string('1', 65) });

        var error = Assert.Single(errors);
        Assert.Equal("version", error.Field);
        Assert.Equal("too_long", error.Code);
    }

    [Theory]
    [InlineData("../run.exe")]
    [InlineData("bin/../../run.exe")]
    [InlineData("C:\\run.exe")]
    public void Validate_EntryPoint_inseguro_devuelve_invalid_path(string entryPoint)
    {
        var errors = CreatePackageValidator.Validate(Valid() with { EntryPoint = entryPoint });

        var error = Assert.Single(errors);
        Assert.Equal("entryPoint", error.Field);
        Assert.Equal("invalid_path", error.Code);
    }

    [Fact]
    public void Validate_SourceDirectory_relativa_devuelve_invalid_path()
    {
        var errors = CreatePackageValidator.Validate(Valid() with { SourceDirectory = "games/quake3" });

        var error = Assert.Single(errors);
        Assert.Equal("sourceDirectory", error.Field);
        Assert.Equal("invalid_path", error.Code);
    }

    [Fact]
    public void Validate_Titulo_demasiado_largo_devuelve_too_long()
    {
        var errors = CreatePackageValidator.Validate(Valid() with { Title = new string('t', 201) });

        var error = Assert.Single(errors);
        Assert.Equal("title", error.Field);
        Assert.Equal("too_long", error.Code);
    }

    [Fact]
    public void Validate_Argumentos_demasiado_largos_devuelven_too_long()
    {
        var errors = CreatePackageValidator.Validate(Valid() with { Arguments = new string('a', 1025) });

        var error = Assert.Single(errors);
        Assert.Equal("arguments", error.Field);
        Assert.Equal("too_long", error.Code);
    }

    [Fact]
    public void Validate_Devuelve_todos_los_campos_incorrectos_a_la_vez()
    {
        var request = new CreatePackageRequest
        {
            GameId = "quake 3",
            Version = "v1",
            EntryPoint = "../run.exe",
        };

        var errors = CreatePackageValidator.Validate(request);

        Assert.Equal(5, errors.Count);
        Assert.Contains(errors, e => e.Field == "gameId" && e.Code == "invalid_format");
        Assert.Contains(errors, e => e.Field == "title" && e.Code == "required");
        Assert.Contains(errors, e => e.Field == "version" && e.Code == "invalid_format");
        Assert.Contains(errors, e => e.Field == "sourceDirectory" && e.Code == "required");
        Assert.Contains(errors, e => e.Field == "entryPoint" && e.Code == "invalid_path");
    }
}
