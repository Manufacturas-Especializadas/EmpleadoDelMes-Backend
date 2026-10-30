using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Domain.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Employees;

[TestClass]
public sealed class EmployeeServiceTests
{
    private sealed class FakeRepository : IEmployeeRepository
    {
        public List<EmployeeDto> Rows { get; } = [];
        public EmployeeError? Failure { get; set; }
        private int revision;
        private void Check(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Failure is { } error) throw new EmployeePersistenceException(error, new Exception("SQL detalle interno"));
        }
        public Task<EmployeeDto?> GetByIdAsync(Guid id, CancellationToken token)
        { Check(token); return Task.FromResult(Rows.SingleOrDefault(e => e.Id == id)); }
        public Task<EmployeeDto?> GetByPayrollNumberAsync(string payroll, CancellationToken token)
        { Check(token); return Task.FromResult(Rows.SingleOrDefault(e => e.PayrollNumber == payroll)); }
        public Task<EmployeePage> SearchAsync(string term, int page, int size, CancellationToken token)
        {
            Check(token);
            var found = Rows.Where(e => e.PayrollNumber.Contains(term) || e.FullName.Contains(term))
                .OrderBy(e => e.PayrollNumber).ThenBy(e => e.Id).ToArray();
            return Task.FromResult(new EmployeePage(found.Skip((page - 1) * size).Take(size).ToArray(), found.Length, page, size));
        }
        public Task<EmployeeDto> CreateAsync(Employee employee, CancellationToken token)
        {
            Check(token);
            if (Rows.Any(e => e.PayrollNumber == employee.PayrollNumber)) throw new EmployeePersistenceException(EmployeeError.Duplicate);
            var row = new EmployeeDto(employee.Id, employee.PayrollNumber, employee.FullName, Convert.ToBase64String(BitConverter.GetBytes((long)++revision)));
            Rows.Add(row);
            return Task.FromResult(row);
        }
        public Task<EmployeeDto> UpdateNameAsync(Employee employee, byte[] version, CancellationToken token)
        {
            Check(token);
            var index = Rows.FindIndex(e => e.Id == employee.Id);
            if (index < 0 || Rows[index].Version != Convert.ToBase64String(version)) throw new EmployeePersistenceException(EmployeeError.Concurrency);
            var row = Rows[index] with { FullName = employee.FullName, Version = Convert.ToBase64String(BitConverter.GetBytes((long)++revision)) };
            Rows[index] = row;
            return Task.FromResult(row);
        }
    }

    [TestMethod]
    [DataRow("123")]
    [DataRow("007")]
    public async Task CrearYConsultar_ConservaNomina(string payroll)
    {
        var service = new EmployeeService(new FakeRepository());
        var result = await service.CreateEmployee(payroll, "Ana López");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(payroll, result.Value!.PayrollNumber);
        Assert.AreEqual(result.Value, (await service.GetEmployeeById(result.Value.Id)).Value);
        Assert.AreEqual(result.Value, (await service.GetEmployeeByPayrollNumber(" " + payroll + " ")).Value);
        Assert.AreEqual(EmployeeError.Duplicate, (await service.CreateEmployee(payroll, "Otra persona")).Error);
    }

    [TestMethod]
    [DataRow("", "Ana")]
    [DataRow(null, "Ana")]
    [DataRow("123", " ")]
    [DataRow("123", null)]
    public async Task Crear_RechazaDatosVaciosSinPersistir(string? payroll, string? name)
    {
        var repository = new FakeRepository();
        var result = await new EmployeeService(repository).CreateEmployee(payroll, name);
        Assert.AreEqual(EmployeeError.Validation, result.Error);
        Assert.AreEqual(0, repository.Rows.Count);
    }

    [TestMethod]
    public async Task Crear_RechazaLongitudesIncompatiblesConElEsquema()
    {
        var service = new EmployeeService(new FakeRepository());
        Assert.AreEqual(EmployeeError.Validation, (await service.CreateEmployee(new string('1', 33), "Ana")).Error);
        Assert.AreEqual(EmployeeError.Validation, (await service.CreateEmployee("123", new string('a', 201))).Error);
    }

    [TestMethod]
    [DataRow("Ana", 2)]
    [DataRow("00", 2)]
    [DataRow("nadie", 0)]
    public async Task Buscar_FiltraYDevuelveTotal(string term, int total)
    {
        var service = new EmployeeService(new FakeRepository());
        await service.CreateEmployee("001", "Ana López");
        await service.CreateEmployee("002", "Ana Pérez");
        await service.CreateEmployee("123", "Luis");
        var result = await service.SearchEmployees(term, 1, 1);
        Assert.AreEqual(total, result.Value!.Total);
        Assert.AreEqual(Math.Min(1, total), result.Value.Items.Count);
    }

    [TestMethod]
    public async Task Buscar_PaginaEnOrdenConsistente()
    {
        var service = new EmployeeService(new FakeRepository());
        await service.CreateEmployee("003", "C");
        await service.CreateEmployee("001", "A");
        await service.CreateEmployee("002", "B");
        var page = (await service.SearchEmployees(null, 2, 2)).Value!;
        Assert.AreEqual(3, page.Total);
        Assert.AreEqual("003", page.Items.Single().PayrollNumber);
        Assert.AreEqual(2, page.PageNumber);
    }

    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(1, 0)]
    [DataRow(1, 101)]
    [DataRow(int.MaxValue, 100)]
    public async Task Buscar_RechazaPaginacionInvalida(int page, int size)
    {
        Assert.AreEqual(EmployeeError.Validation, (await new EmployeeService(new FakeRepository()).SearchEmployees(null, page, size)).Error);
    }

    [TestMethod]
    public async Task Actualizar_ConservaNominaYRechazaVersionAnterior()
    {
        var service = new EmployeeService(new FakeRepository());
        var original = (await service.CreateEmployee("007", "Ana")).Value!;
        var updated = await service.UpdateEmployeeName(original.Id, "Ana López", original.Version);
        Assert.IsTrue(updated.IsSuccess);
        Assert.AreEqual("007", updated.Value!.PayrollNumber);
        Assert.AreEqual("Ana López", updated.Value.FullName);
        Assert.AreEqual(EmployeeError.Concurrency, (await service.UpdateEmployeeName(original.Id, "Cambio obsoleto", original.Version)).Error);
        Assert.AreEqual("Ana López", (await service.GetEmployeeById(original.Id)).Value!.FullName);
        Assert.AreEqual(EmployeeError.Validation, (await service.UpdateEmployeeName(original.Id, "Ana", "incorrecta")).Error);
        Assert.AreEqual(EmployeeError.Validation, (await service.UpdateEmployeeName(original.Id, " ", updated.Value.Version)).Error);
    }

    [TestMethod]
    public async Task ConsultarYActualizar_InexistenteDevuelveResultadoControlado()
    {
        var service = new EmployeeService(new FakeRepository());
        Assert.AreEqual(EmployeeError.NotFound, (await service.GetEmployeeById(Guid.NewGuid())).Error);
        Assert.AreEqual(EmployeeError.NotFound, (await service.GetEmployeeByPayrollNumber("999")).Error);
        Assert.AreEqual(EmployeeError.NotFound, (await service.UpdateEmployeeName(Guid.NewGuid(), "Ana", Convert.ToBase64String(new byte[8]))).Error);
    }

    [TestMethod]
    [DataRow(EmployeeError.Duplicate)]
    [DataRow(EmployeeError.Persistence)]
    public async Task Persistencia_NoExponeDetallesInternos(EmployeeError failure)
    {
        var service = new EmployeeService(new FakeRepository { Failure = failure });
        var result = await service.CreateEmployee("007", "Ana");
        Assert.AreEqual(failure, result.Error);
        Assert.IsFalse(result.Message!.Contains("SQL"));
        Assert.IsFalse(result.IsSuccess);
    }
}
