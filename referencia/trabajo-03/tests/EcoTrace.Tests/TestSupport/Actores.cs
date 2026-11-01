namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Quién hace una llamada en una prueba. Cada actor sabe emitir su token (firmado con la clave de prueba de Identity),
/// y así las pruebas dicen "el supervisor del transportista hace esto" en vez de armar tokens a mano.
/// </summary>
public abstract record Actor
{
    /// <summary>Un administrador de la organización que opera la plataforma. Crea organizaciones y ve todo (lectura).</summary>
    public static Actor Operador { get; } = new OperadorDePlataforma();

    /// <summary>Un servicio interno: su token sirve para un solo módulo, el del cliente HTTP al que se envía.</summary>
    public static Actor ServicioInterno { get; } = new ServicioInternoActor();

    public abstract string TokenPara(string audiencia);
}

/// <summary>Una persona de una organización, con su rol. El <c>sub</c> del token es su UserId.</summary>
public sealed record Persona(Guid TenantId, string Rol) : Actor
{
    public Guid UserId { get; init; } = Guid.NewGuid();

    public override string TokenPara(string audiencia) =>
        SeguridadDePrueba.Emitir(Rol, TenantId, sub: UserId.ToString());
}

public sealed record OperadorDePlataforma : Actor
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public override string TokenPara(string audiencia) =>
        SeguridadDePrueba.Emitir(Rol.Administrador, _tenantId, plataforma: true);
}

public sealed record ServicioInternoActor : Actor
{
    public override string TokenPara(string audiencia) => SeguridadDePrueba.EmitirServicio(audiencia);
}

/// <summary>Un actor sin token: la llamada va anónima.</summary>
public sealed record Anonimo : Actor
{
    public override string TokenPara(string audiencia) => string.Empty;
}
