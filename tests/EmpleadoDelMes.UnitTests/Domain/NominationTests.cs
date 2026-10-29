using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Domain;

[TestClass]
public sealed class NominationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static Nomination Create(NominationQualities qualities = NominationQualities.Teamwork,
        string justification = "Colabora con el equipo.") => new(Guid.NewGuid(), Guid.NewGuid(),
            new Employee(Guid.NewGuid(), "007", "Ana"), new Employee(Guid.NewGuid(), "008", "Luis"),
            Guid.NewGuid(), qualities, justification, Now);

    [TestMethod]
    public void Crear_RechazaAutopostulacionPorIdentidad()
    {
        var id = Guid.NewGuid();
        var sponsor = new Employee(id, "007", "Ana");
        var candidate = new Employee(id, "007", "Ana López");
        Assert.Throws<ArgumentException>(() => new Nomination(Guid.NewGuid(), Guid.NewGuid(), sponsor,
            candidate, Guid.NewGuid(), NominationQualities.Teamwork, "Justificación", Now));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(-1)]
    public void Crear_RechazaCualidadesVaciasODesconocidas(int qualities)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create((NominationQualities)qualities));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(15)]
    public void Crear_AceptaCualidadesValidasYJustificacionDe500Caracteres(int qualities)
    {
        var nomination = Create((NominationQualities)qualities, new string('a', 500));
        Assert.AreEqual((NominationQualities)qualities, nomination.Qualities);
        Assert.AreEqual(500, nomination.Justification.Length);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow(null)]
    public void Crear_RechazaJustificacionVacia(string? justification)
    {
        Assert.Throws<ArgumentException>(() => Create(justification: justification!));
    }

    [TestMethod]
    public void Crear_RechazaJustificacionMayorA500()
    {
        Assert.Throws<ArgumentException>(() => Create(justification: new string('a', 501)));
    }

    [TestMethod]
    public void Anular_ConservaIdentidadYContenido()
    {
        var nomination = Create();
        var id = nomination.Id;
        var campaignId = nomination.CampaignId;
        nomination.Cancel();
        nomination.Cancel();
        Assert.IsTrue(nomination.IsCancelled);
        Assert.AreEqual(id, nomination.Id);
        Assert.AreEqual(campaignId, nomination.CampaignId);
        Assert.AreEqual("Colabora con el equipo.", nomination.Justification);
        Assert.AreEqual(Now, nomination.RegisteredAtUtc);
    }

    [TestMethod]
    public void Instantaneas_NoCambianAlCorregirElCatalogo()
    {
        var sponsor = new Employee(Guid.NewGuid(), "007", "Ana");
        var candidate = new Employee(Guid.NewGuid(), "008", "Luis");
        var nomination = new Nomination(Guid.NewGuid(), Guid.NewGuid(), sponsor, candidate,
            Guid.NewGuid(), NominationQualities.Teamwork, "Justificación", Now);
        sponsor.CorrectName("Ana López");
        candidate.CorrectName("Luis Pérez");
        Assert.AreEqual("Ana", nomination.SponsorFullName);
        Assert.AreEqual("Luis", nomination.CandidateFullName);
        Assert.AreEqual("007", nomination.SponsorPayrollNumber);
        Assert.AreEqual("008", nomination.CandidatePayrollNumber);
    }

    [TestMethod]
    public void Corregir_ValidaAntesDeCambiarElEstado()
    {
        var nomination = Create();
        Assert.Throws<ArgumentException>(() => nomination.Correct(NominationQualities.ProblemSolving, new string('a', 501)));
        Assert.AreEqual(NominationQualities.Teamwork, nomination.Qualities);
        Assert.Throws<ArgumentOutOfRangeException>(() => nomination.Correct(NominationQualities.None, "Nuevo"));
        Assert.AreEqual("Colabora con el equipo.", nomination.Justification);
        nomination.Correct(NominationQualities.ProblemSolving, "Texto corregido");
        Assert.AreEqual("Texto corregido", nomination.Justification);
        Assert.AreEqual(NominationQualities.ProblemSolving, nomination.Qualities);
    }

    [TestMethod]
    public void Crear_PermiteCombinarCualidadesConSusValoresAprobados()
    {
        Assert.AreEqual(1, (int)NominationQualities.Teamwork);
        Assert.AreEqual(2, (int)NominationQualities.ProblemSolving);
        Assert.AreEqual(4, (int)NominationQualities.Commitment);
        Assert.AreEqual(8, (int)NominationQualities.Initiative);
        var qualities = NominationQualities.Teamwork | NominationQualities.Initiative;
        Assert.AreEqual(qualities, Create(qualities).Qualities);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CorregirIdentidad_ActualizaEmpleadoYSuInstantanea(bool changeSponsor)
    {
        var sponsor = new Employee(Guid.NewGuid(), "007", "Ana");
        var candidate = new Employee(Guid.NewGuid(), "008", "Luis");
        var nomination = new Nomination(Guid.NewGuid(), Guid.NewGuid(), sponsor, candidate,
            Guid.NewGuid(), NominationQualities.Teamwork, "Original", Now);
        var replacement = new Employee(Guid.NewGuid(), "009", "María");
        var expectedSponsor = changeSponsor ? replacement : sponsor;
        var expectedCandidate = changeSponsor ? candidate : replacement;

        nomination.Correct(nomination.CampaignId, expectedSponsor, expectedCandidate,
            nomination.Qualities, nomination.Justification);

        Assert.AreEqual(expectedSponsor.Id, nomination.SponsorEmployeeId);
        Assert.AreEqual(expectedSponsor.PayrollNumber, nomination.SponsorPayrollNumber);
        Assert.AreEqual(expectedSponsor.FullName, nomination.SponsorFullName);
        Assert.AreEqual(expectedCandidate.Id, nomination.CandidateEmployeeId);
        Assert.AreEqual(expectedCandidate.PayrollNumber, nomination.CandidatePayrollNumber);
        Assert.AreEqual(expectedCandidate.FullName, nomination.CandidateFullName);
        replacement.CorrectName("María Pérez");
        Assert.AreEqual("María", changeSponsor ? nomination.SponsorFullName : nomination.CandidateFullName);
    }

    [TestMethod]
    public void Corregir_ReasignaConvocatoriaSinCambiarDatosDeCreacionNiOpiniones()
    {
        var nomination = Create();
        var id = nomination.Id;
        var author = nomination.CreatedByUserId;
        var opinion = new CandidateOpinion(Guid.NewGuid(), nomination.CampaignId, nomination.CandidateEmployeeId,
            OpinionSource.Quality, "Opinión original", Guid.NewGuid(), Now);
        var oldCampaign = opinion.CampaignId;
        var oldCandidate = opinion.CandidateEmployeeId;
        var newCampaign = Guid.NewGuid();
        var sponsor = new Employee(Guid.NewGuid(), "010", "Nueva patrocinadora");
        var candidate = new Employee(Guid.NewGuid(), "011", "Nuevo candidato");
        var qualities = NominationQualities.Commitment | NominationQualities.ProblemSolving;
        nomination.Cancel();
        nomination.Correct(newCampaign, sponsor, candidate, qualities, new string('a', 500));

        Assert.AreEqual(newCampaign, nomination.CampaignId);
        Assert.AreEqual(qualities, nomination.Qualities);
        Assert.AreEqual(500, nomination.Justification.Length);
        Assert.AreEqual(id, nomination.Id);
        Assert.AreEqual(author, nomination.CreatedByUserId);
        Assert.AreEqual(Now, nomination.RegisteredAtUtc);
        Assert.IsTrue(nomination.IsCancelled);
        Assert.AreEqual(oldCampaign, opinion.CampaignId);
        Assert.AreEqual(oldCandidate, opinion.CandidateEmployeeId);
        Assert.AreEqual("Opinión original", opinion.Content);
    }

    [TestMethod]
    public void Corregir_ActualizaInstantaneasDeLosMismosEmpleados()
    {
        var sponsor = new Employee(Guid.NewGuid(), "007", "Ana");
        var candidate = new Employee(Guid.NewGuid(), "008", "Luis");
        var nomination = new Nomination(Guid.NewGuid(), Guid.NewGuid(), sponsor, candidate,
            Guid.NewGuid(), NominationQualities.Teamwork, "Original", Now);
        sponsor.CorrectName("Ana López");
        candidate.CorrectName("Luis Pérez");
        nomination.Correct(nomination.CampaignId, sponsor, candidate, nomination.Qualities, nomination.Justification);
        Assert.AreEqual(sponsor.Id, nomination.SponsorEmployeeId);
        Assert.AreEqual(candidate.Id, nomination.CandidateEmployeeId);
        Assert.AreEqual("Ana López", nomination.SponsorFullName);
        Assert.AreEqual("Luis Pérez", nomination.CandidateFullName);
    }

    [TestMethod]
    [DataRow("selfSponsor")]
    [DataRow("selfCandidate")]
    [DataRow("campaign")]
    [DataRow("sponsorNull")]
    [DataRow("candidateNull")]
    [DataRow("noQualities")]
    [DataRow("unknownQualities")]
    [DataRow("emptyJustification")]
    [DataRow("longJustification")]
    public void CorreccionInvalida_NoModificaNingunDato(string invalidField)
    {
        var nomination = Create();
        var original = (nomination.CampaignId, nomination.SponsorEmployeeId, nomination.CandidateEmployeeId,
            nomination.SponsorPayrollNumber, nomination.SponsorFullName, nomination.CandidatePayrollNumber,
            nomination.CandidateFullName, nomination.Qualities, nomination.Justification);
        var campaign = Guid.NewGuid();
        Employee? sponsor = new(Guid.NewGuid(), "010", "Nueva patrocinadora");
        Employee? candidate = new(Guid.NewGuid(), "011", "Nuevo candidato");
        var qualities = NominationQualities.Commitment;
        var justification = "Nueva justificación";
        switch (invalidField)
        {
            case "selfSponsor":
                sponsor = new Employee(nomination.CandidateEmployeeId, "008", "Luis");
                candidate = new Employee(nomination.CandidateEmployeeId, "008", "Luis");
                break;
            case "selfCandidate":
                sponsor = new Employee(nomination.SponsorEmployeeId, "007", "Ana");
                candidate = new Employee(nomination.SponsorEmployeeId, "007", "Ana");
                break;
            case "campaign": campaign = Guid.Empty; break;
            case "sponsorNull": sponsor = null; break;
            case "candidateNull": candidate = null; break;
            case "noQualities": qualities = NominationQualities.None; break;
            case "unknownQualities": qualities = (NominationQualities)17; break;
            case "emptyJustification": justification = " "; break;
            case "longJustification": justification = new string('a', 501); break;
        }

        Assert.Throws<ArgumentException>(() => nomination.Correct(campaign, sponsor!, candidate!, qualities, justification));
        Assert.AreEqual(original, (nomination.CampaignId, nomination.SponsorEmployeeId, nomination.CandidateEmployeeId,
            nomination.SponsorPayrollNumber, nomination.SponsorFullName, nomination.CandidatePayrollNumber,
            nomination.CandidateFullName, nomination.Qualities, nomination.Justification));
    }
}
