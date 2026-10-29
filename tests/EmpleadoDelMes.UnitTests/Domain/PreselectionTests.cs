using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Domain;

[TestClass]
public sealed class PreselectionTests
{
    [TestMethod]
    public void RetirarYReincorporar_ConservaIdentidadYOpinionExistente()
    {
        var campaignId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var selection = new Preselection(campaignId, candidateId, actor, now);
        var opinion = new CandidateOpinion(Guid.NewGuid(), campaignId, candidateId,
            OpinionSource.Safety, "Opinión existente", actor, now);
        Assert.IsTrue(selection.IsSelected);
        selection.Withdraw(actor, now.AddHours(1));
        Assert.IsFalse(selection.IsSelected);
        Assert.AreEqual(now.AddHours(1), selection.LastChangedAtUtc);
        var nextActor = Guid.NewGuid();
        selection.Select(nextActor, now.AddHours(2));
        Assert.IsTrue(selection.IsSelected);
        Assert.AreEqual(nextActor, selection.LastChangedByUserId);
        Assert.AreEqual(now.AddHours(2), selection.LastChangedAtUtc);
        Assert.AreEqual(campaignId, selection.CampaignId);
        Assert.AreEqual(candidateId, selection.CandidateEmployeeId);
        Assert.AreEqual(selection.CampaignId, opinion.CampaignId);
        Assert.AreEqual(selection.CandidateEmployeeId, opinion.CandidateEmployeeId);
        Assert.AreEqual("Opinión existente", opinion.Content);
    }

    [TestMethod]
    public void Retirar_SinActorNoModificaEstado()
    {
        var actor = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var selection = new Preselection(Guid.NewGuid(), Guid.NewGuid(), actor, now);
        Assert.Throws<ArgumentException>(() => selection.Withdraw(Guid.Empty, now.AddHours(1)));
        Assert.IsTrue(selection.IsSelected);
        Assert.AreEqual(actor, selection.LastChangedByUserId);
        Assert.AreEqual(now, selection.LastChangedAtUtc);
    }
}
