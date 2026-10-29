using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Domain;

[TestClass]
public sealed class CampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static Campaign Create() => new(Guid.NewGuid(), new DateOnly(2026, 10, 29), Now);

    [TestMethod]
    public void Crear_NormalizaMesYComienzaConPeriodosCerrados()
    {
        var campaign = Create();
        Assert.AreEqual(new DateOnly(2026, 10, 1), campaign.EvaluationMonth);
        Assert.IsFalse(campaign.CanAcceptNominations(Now));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
    }

    [TestMethod]
    public void Periodos_SonIndependientesYRespetanLimitesProgramados()
    {
        var campaign = Create();
        campaign.ScheduleNominations(Now, Now.AddHours(1));
        campaign.EnableAutomaticNominations();
        campaign.ScheduleOpinions(Now.AddHours(2), Now.AddHours(3));
        campaign.EnableAutomaticOpinions();
        Assert.IsFalse(campaign.CanAcceptNominations(Now.AddTicks(-1)));
        Assert.IsTrue(campaign.CanAcceptNominations(Now));
        Assert.IsFalse(campaign.CanAcceptNominations(Now.AddHours(1)));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
        Assert.IsTrue(campaign.CanAcceptOpinions(Now.AddHours(2)));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now.AddHours(3)));
    }

    [TestMethod]
    public void ModoManual_PermaneceHastaHabilitarAutomatico()
    {
        var campaign = Create();
        campaign.ScheduleNominations(Now, Now.AddHours(1));
        campaign.EnableAutomaticNominations();
        campaign.CloseNominations();
        Assert.IsFalse(campaign.CanAcceptNominations(Now));
        campaign.OpenNominations();
        Assert.IsTrue(campaign.CanAcceptNominations(Now.AddDays(1)));
        campaign.EnableAutomaticNominations();
        Assert.IsFalse(campaign.CanAcceptNominations(Now.AddDays(1)));
        campaign.OpenOpinions();
        Assert.IsTrue(campaign.CanAcceptOpinions(Now));
        campaign.CloseOpinions();
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
    }

    [TestMethod]
    public void ArchivarYReabrir_ConservaHorarioPeroExigeHabilitacionExplicita()
    {
        var campaign = Create();
        campaign.ScheduleNominations(Now, Now.AddDays(1));
        campaign.ScheduleOpinions(Now, Now.AddDays(1));
        campaign.EnableAutomaticNominations();
        campaign.EnableAutomaticOpinions();
        campaign.Archive();
        Assert.IsTrue(campaign.IsArchived);
        Assert.IsFalse(campaign.CanAcceptNominations(Now));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
        Assert.Throws<InvalidOperationException>(() => campaign.OpenNominations());
        Assert.Throws<InvalidOperationException>(() => campaign.OpenOpinions());
        Assert.Throws<InvalidOperationException>(() => campaign.EnableAutomaticNominations());
        Assert.Throws<InvalidOperationException>(() => campaign.EnableAutomaticOpinions());
        campaign.Reopen();
        Assert.IsFalse(campaign.IsArchived);
        Assert.IsFalse(campaign.CanAcceptNominations(Now));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
        Assert.AreEqual(CampaignPeriodMode.ManuallyClosed, campaign.NominationPeriod.Mode);
        Assert.AreEqual(CampaignPeriodMode.ManuallyClosed, campaign.OpinionPeriod.Mode);
        Assert.AreEqual(Now, campaign.NominationPeriod.ScheduledOpeningAtUtc);
        Assert.AreEqual(Now, campaign.OpinionPeriod.ScheduledOpeningAtUtc);
        campaign.EnableAutomaticNominations();
        Assert.IsTrue(campaign.CanAcceptNominations(Now));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
    }

    [TestMethod]
    public void Programar_RechazaRangosInvalidosYAutomaticoSinHorario()
    {
        var campaign = Create();
        Assert.Throws<InvalidOperationException>(() => campaign.EnableAutomaticNominations());
        Assert.Throws<InvalidOperationException>(() => campaign.EnableAutomaticOpinions());
        Assert.Throws<ArgumentException>(() => campaign.ScheduleNominations(Now, Now));
        Assert.Throws<ArgumentException>(() => campaign.ScheduleOpinions(Now, Now.AddHours(-1)));
    }

    [TestMethod]
    public void Fechas_SeNormalizanAUtc()
    {
        var local = Now.ToOffset(TimeSpan.FromHours(-6));
        var campaign = new Campaign(Guid.NewGuid(), new DateOnly(2026, 10, 1), local);
        campaign.ScheduleNominations(local, local.AddHours(1));
        Assert.AreEqual(TimeSpan.Zero, campaign.CreatedAtUtc.Offset);
        Assert.AreEqual(Now, campaign.CreatedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, campaign.NominationPeriod.ScheduledOpeningAtUtc!.Value.Offset);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ControlManual_TienePrioridadHastaRegresarExpresamenteAAutomatico(bool nominations)
    {
        var campaign = Create();
        Action<DateTimeOffset, DateTimeOffset> schedule = nominations
            ? campaign.ScheduleNominations : campaign.ScheduleOpinions;
        Action automatic = nominations ? campaign.EnableAutomaticNominations : campaign.EnableAutomaticOpinions;
        Action open = nominations ? campaign.OpenNominations : campaign.OpenOpinions;
        Action close = nominations ? campaign.CloseNominations : campaign.CloseOpinions;
        Func<DateTimeOffset, bool> accepts = nominations ? campaign.CanAcceptNominations : campaign.CanAcceptOpinions;
        schedule(Now, Now.AddHours(1));
        automatic();
        Assert.IsFalse(accepts(Now.AddTicks(-1)));
        Assert.IsTrue(accepts(Now));
        Assert.IsTrue(accepts(Now.AddMinutes(30)));
        Assert.IsFalse(accepts(Now.AddHours(1)));

        close();
        Assert.IsFalse(accepts(Now.AddMinutes(30)));
        schedule(Now.AddHours(-1), Now.AddHours(2));
        Assert.IsFalse(accepts(Now));
        automatic();
        Assert.IsTrue(accepts(Now));

        open();
        Assert.IsTrue(accepts(Now.AddDays(-1)));
        Assert.IsTrue(accepts(Now.AddDays(1)));
        schedule(Now, Now.AddHours(1));
        Assert.IsTrue(accepts(Now.AddDays(1)));
        automatic();
        Assert.IsFalse(accepts(Now.AddDays(1)));
    }

    [TestMethod]
    public void ArchivarPeriodosAbiertosManualmente_ReabrirNoRestauraLaApertura()
    {
        var campaign = Create();
        campaign.OpenNominations();
        campaign.OpenOpinions();
        campaign.Archive();
        Assert.IsFalse(campaign.CanAcceptNominations(Now));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
        campaign.Reopen();
        Assert.IsFalse(campaign.CanAcceptNominations(Now));
        Assert.IsFalse(campaign.CanAcceptOpinions(Now));
        campaign.OpenOpinions();
        Assert.IsTrue(campaign.CanAcceptOpinions(Now));
        Assert.IsFalse(campaign.CanAcceptNominations(Now));
    }
}
