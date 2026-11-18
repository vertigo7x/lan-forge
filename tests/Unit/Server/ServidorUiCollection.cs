namespace LanForge.UnitTests.Server;

/// <summary>
/// Coleccion de tests de UI del servidor: no se ejecutan en paralelo entre si porque
/// la aplicacion Avalonia es un singleton por proceso y las ventanas solo se pueden
/// construir en el hilo de UI.
///
/// No necesita un fixture propio: el arranque de Avalonia en headless es comun a todo
/// el proceso y lo gestiona <c>HeadlessAvalonia</c>.
/// </summary>
[CollectionDefinition("Servidor-UI", DisableParallelization = true)]
public sealed class ServidorUiCollection;
