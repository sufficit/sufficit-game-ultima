namespace Ultima.Models.Core;

/// <summary>Versão do contrato. Um pacote de modelos só é carregado se pedir esta mesma versão.</summary>
public static class ModelCore
{
    /// <summary>Incrementar somente em mudança incompatível de <see cref="IModel"/>, <see cref="ModelBuilder"/> ou do formato do descritor.</summary>
    public const int ApiVersion = 1;
}

/// <summary>
/// Interface pública comum a todos os modelos 3D do jogo. Uma implementação vive numa DLL
/// própria (Ultima.Models.*), que depende só deste Core, e é descoberta em tempo de execução.
/// O modelo não desenha nada: devolve um <see cref="ModelDescriptor"/> (dados), que o
/// renderizador genérico do jogo transforma em malhas na GPU.
/// </summary>
public interface IModel
{
    /// <summary>Identificador estável e único, ex.: "tree.pine".</summary>
    string Id { get; }
    /// <summary>Nome legível.</summary>
    string Name { get; }
    /// <summary>Versão do próprio modelo (muda quando a forma muda).</summary>
    int Version { get; }
    /// <summary>Categoria: "vegetation", "rock", "character", "prop"...</summary>
    string Category { get; }
    /// <summary>Versão do Core para a qual o modelo foi escrito (<see cref="ModelCore.ApiVersion"/>).</summary>
    int RequiredCoreApi { get; }
    /// <summary>Monta a descrição do modelo. Não deve ter efeitos colaterais.</summary>
    ModelDescriptor Build(ModelParams parameters);
}

/// <summary>Parâmetros de construção passados pelo jogo.</summary>
public sealed class ModelParams
{
    public int Seed { get; init; }
    /// <summary>Nível de detalhe de 0,25 a 2 (multiplica a tesselação).</summary>
    public double Detail { get; init; } = 1.0;
    public static ModelParams Default { get; } = new();
}

/// <summary>
/// Base recomendada: implementa <see cref="IModel"/> e aplica as regras comuns
/// (base no chão, encaixe entre partes, validação) via <see cref="ModelBuilder"/>.
/// </summary>
public abstract class ModelBase : IModel
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual int Version => 1;
    public abstract string Category { get; }
    public int RequiredCoreApi => ModelCore.ApiVersion;

    public ModelDescriptor Build(ModelParams parameters)
    {
        var builder = new ModelBuilder(Id, Name, Version, Category, parameters ?? ModelParams.Default);
        Compose(builder);
        return builder.Build();
    }

    /// <summary>Descreve as partes do modelo usando o construtor comum.</summary>
    protected abstract void Compose(ModelBuilder b);
}

/// <summary>Erro de regra do Core (ex.: parte flutuando). O modelo é rejeitado e vira marcador.</summary>
public sealed class ModelValidationException(string message) : Exception(message);
