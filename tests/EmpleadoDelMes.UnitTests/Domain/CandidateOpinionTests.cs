using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Domain;

[TestClass]
public sealed class CandidateOpinionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static CandidateOpinion Create(string content, OpinionSource source = OpinionSource.HumanResources)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), source, content, Guid.NewGuid(), Now);

    [TestMethod]
    [DataRow(OpinionSource.HumanResources)]
    [DataRow(OpinionSource.Safety)]
    [DataRow(OpinionSource.Quality)]
    [DataRow(OpinionSource.ImmediateSupervisor)]
    public void Crear_AceptaOrigenValidoY500Caracteres(OpinionSource source)
    {
        var opinion = Create(new string('a', 500), source);
        Assert.AreEqual(source, opinion.OpinionSource);
        Assert.AreEqual(500, opinion.Content.Length);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow(null)]
    public void CrearYEditar_RechazaContenidoVacio(string? content)
    {
        Assert.Throws<ArgumentException>(() => Create(content!));
        var opinion = Create("Original");
        Assert.Throws<ArgumentException>(() => opinion.EditContent(content!, Now.AddHours(1)));
        Assert.AreEqual("Original", opinion.Content);
        Assert.AreEqual(Now, opinion.LastModifiedAtUtc);
    }

    [TestMethod]
    public void CrearYEditar_RechazaMasDe500Caracteres()
    {
        Assert.Throws<ArgumentException>(() => Create(new string('a', 501)));
        var opinion = Create("Original");
        Assert.Throws<ArgumentException>(() => opinion.EditContent(new string('a', 501), Now.AddHours(1)));
        Assert.AreEqual("Original", opinion.Content);
        Assert.AreEqual(Now, opinion.LastModifiedAtUtc);
    }

    [TestMethod]
    public void Editar_ConservaIdentidadAutorYCreacionYActualizaFechaUtc()
    {
        var opinion = Create("Original");
        var id = opinion.Id;
        var author = opinion.CreatedByUserId;
        opinion.EditContent(new string('b', 500), Now.AddHours(1).ToOffset(TimeSpan.FromHours(-6)));
        Assert.AreEqual(id, opinion.Id);
        Assert.AreEqual(author, opinion.CreatedByUserId);
        Assert.AreEqual(Now, opinion.CreatedAtUtc);
        Assert.AreEqual(Now.AddHours(1), opinion.LastModifiedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, opinion.LastModifiedAtUtc.Offset);
        Assert.AreEqual(500, opinion.Content.Length);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(5)]
    public void Crear_RechazaOrigenDesconocido(int source)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create("Opinión", (OpinionSource)source));
    }
}
