using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Persistence;

[TestClass]
public sealed class MaterializationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer().Options);

    // Ejecuta el materializador real de EF sobre un ValueBuffer en memoria.
    // No usa un proveedor InMemory, conexión ni lector SQL simulado.
    private static object Materialize(ApplicationDbContext context, object original)
    {
        var type = context.Model.FindEntityType(original.GetType())!;
        var properties = type.GetFlattenedProperties().ToArray();
        var values = new object?[properties.Max(p => p.GetIndex()) + 1];
        FillBuffer(type, original, values);
        var buffer = new ValueBuffer(values);
        var result = context.GetService<IStructuralTypeMaterializerSource>()
            .GetMaterializer(type)(new MaterializationContext(buffer, context));
        AssertValues(type, original, result);
        return result;
    }

    private static void FillBuffer(ITypeBase type, object instance, object?[] values)
    {
        foreach (var property in type.GetProperties())
        {
            values[property.GetIndex()] = property.IsShadowProperty()
                ? new byte[8] : property.PropertyInfo!.GetValue(instance);
        }
        foreach (var complex in type.GetComplexProperties())
        {
            FillBuffer(complex.ComplexType, complex.PropertyInfo!.GetValue(instance)!, values);
        }
    }

    private static void AssertValues(ITypeBase type, object expected, object actual)
    {
        foreach (var property in type.GetProperties().Where(p => !p.IsShadowProperty()))
        {
            Assert.AreEqual(property.PropertyInfo!.GetValue(expected), property.PropertyInfo.GetValue(actual), property.Name);
        }
        foreach (var complex in type.GetComplexProperties())
        {
            AssertValues(complex.ComplexType, complex.PropertyInfo!.GetValue(expected)!, complex.PropertyInfo.GetValue(actual)!);
        }
    }

    [TestMethod]
    public void Entidades_MaterializanPropiedadesEncapsuladasSinPerderEstadoHistorico()
    {
        using var context = CreateContext();
        var sponsor = new Employee(Guid.NewGuid(), "007", "Ana");
        var candidate = new Employee(Guid.NewGuid(), "008", "Luis");
        var campaign = new Campaign(Guid.NewGuid(), new DateOnly(2026, 10, 1), Now);
        var nomination = new Nomination(Guid.NewGuid(), campaign.Id, sponsor, candidate,
            Guid.NewGuid(), NominationQualities.Teamwork | NominationQualities.Initiative, "Justificación", Now);
        nomination.Cancel();
        var preselection = new Preselection(campaign.Id, candidate.Id, Guid.NewGuid(), Now);
        preselection.Withdraw(Guid.NewGuid(), Now.AddHours(1));
        var opinion = new CandidateOpinion(Guid.NewGuid(), campaign.Id, candidate.Id,
            OpinionSource.Safety, "Original", Guid.NewGuid(), Now);
        opinion.EditContent("Editada", Now.AddHours(1));
        var audit = new NominationAuditEntry(Guid.NewGuid(), nomination.Id, Guid.NewGuid(),
            NominationAuditAction.Correction, Now, "Motivo", "Detalle de corrección");
        foreach (var entity in new object[] { sponsor, campaign, nomination, preselection, opinion, audit })
        {
            Materialize(context, entity);
        }
    }

    [TestMethod]
    public void Campaign_MaterializaPeriodosInmutablesYDetectaSuReemplazo()
    {
        using var context = CreateContext();
        var original = new Campaign(Guid.NewGuid(), new DateOnly(2026, 10, 1), Now);
        original.ScheduleNominations(Now, Now.AddHours(1));
        original.EnableAutomaticNominations();
        original.ScheduleOpinions(Now.AddHours(2), Now.AddHours(3));
        original.OpenOpinions();
        var campaign = (Campaign)Materialize(context, original);
        Assert.AreNotSame(original.NominationPeriod, campaign.NominationPeriod);
        Assert.AreNotSame(original.OpinionPeriod, campaign.OpinionPeriod);
        Assert.IsTrue(campaign.CanAcceptNominations(Now));
        Assert.IsTrue(campaign.CanAcceptOpinions(Now));

        var entry = context.Attach(campaign);
        var previousPeriod = campaign.NominationPeriod;
        campaign.CloseNominations();
        campaign.ScheduleOpinions(Now.AddDays(1), Now.AddDays(2));
        context.ChangeTracker.DetectChanges();
        Assert.AreNotSame(previousPeriod, campaign.NominationPeriod);
        Assert.AreEqual(EntityState.Modified, entry.State);
        Assert.IsTrue(entry.ComplexProperty(x => x.NominationPeriod).Property(x => x.Mode).IsModified);
        Assert.IsTrue(entry.ComplexProperty(x => x.OpinionPeriod).Property(x => x.ScheduledOpeningAtUtc).IsModified);
        Assert.IsFalse(entry.ComplexProperty(x => x.OpinionPeriod).Property(x => x.Mode).IsModified);
    }

    [TestMethod]
    public void RetirarPreseleccion_NoMarcaOpinionesParaEliminar()
    {
        using var context = CreateContext();
        var selection = new Preselection(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var opinion = new CandidateOpinion(Guid.NewGuid(), selection.CampaignId, selection.CandidateEmployeeId,
            OpinionSource.HumanResources, "Opinión", Guid.NewGuid(), Now);
        context.Attach(selection);
        context.Attach(opinion);
        selection.Withdraw(Guid.NewGuid(), Now.AddHours(1));
        context.ChangeTracker.DetectChanges();
        Assert.AreEqual(EntityState.Modified, context.Entry(selection).State);
        Assert.AreEqual(EntityState.Unchanged, context.Entry(opinion).State);
    }
}
