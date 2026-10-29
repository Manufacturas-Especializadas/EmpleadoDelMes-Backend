using EmpleadoDelMes.Domain.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Domain;

[TestClass]
public sealed class EmployeeTests
{
    [TestMethod]
    [DataRow("123")]
    [DataRow("007")]
    public void Crear_ConservaNominaComoTexto(string payroll)
    {
        var employee = new Employee(Guid.NewGuid(), payroll, "Ana López");
        Assert.AreEqual(payroll, employee.PayrollNumber);
        Assert.AreEqual("Ana López", employee.FullName);
    }

    [TestMethod]
    [DataRow("", "Ana")]
    [DataRow("  ", "Ana")]
    [DataRow(null, "Ana")]
    [DataRow("123", "")]
    [DataRow("123", "  ")]
    [DataRow("123", null)]
    public void Crear_RechazaDatosVacios(string? payroll, string? name)
    {
        Assert.Throws<ArgumentException>(() => new Employee(Guid.NewGuid(), payroll!, name!));
    }

    [TestMethod]
    public void CorregirNombre_ConservaIdentidadYNomina()
    {
        var id = Guid.NewGuid();
        var employee = new Employee(id, "007", "Ana");
        employee.CorrectName("Ana López");
        Assert.AreEqual(id, employee.Id);
        Assert.AreEqual("007", employee.PayrollNumber);
        Assert.AreEqual("Ana López", employee.FullName);
        Assert.Throws<ArgumentException>(() => employee.CorrectName(" "));
        Assert.AreEqual("Ana López", employee.FullName);
    }

    [TestMethod]
    public void Crear_RechazaIdentificadorVacio()
    {
        Assert.Throws<ArgumentException>(() => new Employee(Guid.Empty, "123", "Ana"));
    }
}
