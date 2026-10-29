using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Domain;

[TestClass]
public sealed class NominationAuditEntryTests
{
    private static NominationAuditEntry Create(NominationAuditAction action, string reason, string? details = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), action,
            new DateTimeOffset(2026, 10, 29, 6, 0, 0, TimeSpan.FromHours(-6)), reason, details);

    [TestMethod]
    public void CrearCorreccion_ConservaMotivoDetallesYFechaUtc()
    {
        var entry = Create(NominationAuditAction.Correction, "Error de captura", "Justificación: anterior -> nueva");
        Assert.AreEqual("Error de captura", entry.Reason);
        Assert.AreEqual("Justificación: anterior -> nueva", entry.Details);
        Assert.AreEqual(NominationAuditAction.Correction, entry.Action);
        Assert.AreEqual(TimeSpan.Zero, entry.OccurredAtUtc.Offset);
        Assert.AreEqual(12, entry.OccurredAtUtc.Hour);
    }

    [TestMethod]
    public void CrearAnulacion_NoExigeDetallesAdicionales()
    {
        var entry = Create(NominationAuditAction.Cancellation, "Solicitud administrativa");
        Assert.IsNull(entry.Details);
    }

    [TestMethod]
    public void Crear_RechazaAccionInvalidaMotivoVacioOCorreccionSinDetalles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create((NominationAuditAction)0, "Motivo"));
        Assert.Throws<ArgumentException>(() => Create(NominationAuditAction.Cancellation, " "));
        Assert.Throws<ArgumentException>(() => Create(NominationAuditAction.Correction, "Motivo"));
    }
}
