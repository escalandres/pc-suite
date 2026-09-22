namespace PCSuite.Core.Tools;

/// <summary>Agrupación de tools bajo un identificador común.</summary>
public interface IModule
{
    string Id { get; }

    string Name { get; }

    IReadOnlyCollection<ITool> Tools { get; }
}
