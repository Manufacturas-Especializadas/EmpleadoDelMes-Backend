using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Persistence;

[TestClass]
public sealed class ModelTests
{
    // Proveedor SQL Server sin cadena de conexión: estas pruebas nunca abren una conexión.
    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer().Options);

    [TestMethod]
    public void Modelo_ValidaYGeneraScriptSqlEnMemoria()
    {
        using var context = CreateContext();
        var sql = context.Database.GenerateCreateScript();
        Console.WriteLine("Tablas del modelo: " + string.Join(", ",
            context.Model.GetEntityTypes().Select(e => e.GetTableName()).Distinct().Order()));
        foreach (var table in new[] { "Employees", "Campaigns", "Nominations", "Preselections",
            "CandidateOpinions", "NominationAuditEntries", "AspNetUsers", "AspNetRoles",
            "AspNetUserRoles", "AspNetUserClaims", "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens" })
        {
            StringAssert.Contains(sql, $"CREATE TABLE [{table}]");
        }
        StringAssert.Contains(sql, "rowversion");
        StringAssert.Contains(sql, "[SponsorEmployeeId] <> [CandidateEmployeeId]");
        StringAssert.Contains(sql, "([Qualities] & ~15) = 0");
        StringAssert.Contains(sql, "[NominationOpeningAtUtc] < [NominationClosingAtUtc]");
        StringAssert.Contains(sql, "[OpinionOpeningAtUtc] < [OpinionClosingAtUtc]");
        Assert.IsFalse(sql.Contains("CREATE TABLE [CampaignPeriod", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(typeof(Employee), "Id")]
    [DataRow(typeof(Campaign), "Id")]
    [DataRow(typeof(Nomination), "Id")]
    [DataRow(typeof(Preselection), "CampaignId,CandidateEmployeeId")]
    [DataRow(typeof(CandidateOpinion), "Id")]
    [DataRow(typeof(NominationAuditEntry), "Id")]
    public void Entidades_TienenClavesCorrectas(Type type, string names)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(type)!;
        Assert.IsNotNull(entity);
        CollectionAssert.AreEqual(names.Split(','), entity.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.IsTrue(entity.FindPrimaryKey()!.Properties.All(p => p.ClrType == typeof(Guid)));
    }

    [TestMethod]
    [DataRow(typeof(Employee), "PayrollNumber")]
    [DataRow(typeof(Campaign), "EvaluationMonth")]
    [DataRow(typeof(Nomination), "CampaignId,SponsorEmployeeId,CandidateEmployeeId")]
    [DataRow(typeof(CandidateOpinion), "CampaignId,CandidateEmployeeId,OpinionSource")]
    [DataRow(typeof(ApplicationUser), "EmployeeId")]
    public void Indices_SonUnicosYConFiltroSoloParaEmpleadoOpcional(Type type, string properties)
    {
        using var context = CreateContext();
        var index = context.Model.FindEntityType(type)!.GetIndexes().Single(i =>
            string.Join(',', i.Properties.Select(p => p.Name)) == properties);
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual(type == typeof(ApplicationUser) ? "[EmployeeId] IS NOT NULL" : null, index.GetFilter());
    }

    [TestMethod]
    [DataRow(typeof(Nomination), typeof(Campaign), "CampaignId")]
    [DataRow(typeof(Nomination), typeof(Employee), "SponsorEmployeeId")]
    [DataRow(typeof(Nomination), typeof(Employee), "CandidateEmployeeId")]
    [DataRow(typeof(Nomination), typeof(ApplicationUser), "CreatedByUserId")]
    [DataRow(typeof(Preselection), typeof(Campaign), "CampaignId")]
    [DataRow(typeof(Preselection), typeof(Employee), "CandidateEmployeeId")]
    [DataRow(typeof(Preselection), typeof(ApplicationUser), "LastChangedByUserId")]
    [DataRow(typeof(CandidateOpinion), typeof(Preselection), "CampaignId,CandidateEmployeeId")]
    [DataRow(typeof(CandidateOpinion), typeof(ApplicationUser), "CreatedByUserId")]
    [DataRow(typeof(NominationAuditEntry), typeof(Nomination), "NominationId")]
    [DataRow(typeof(NominationAuditEntry), typeof(ApplicationUser), "ActorUserId")]
    [DataRow(typeof(ApplicationUser), typeof(Employee), "EmployeeId")]
    public void Relaciones_UsanPropiedadesRealesSinCascada(Type dependent, Type principal, string properties)
    {
        using var context = CreateContext();
        var fk = context.Model.FindEntityType(dependent)!.GetForeignKeys().Single(f =>
            string.Join(',', f.Properties.Select(p => p.Name)) == properties);
        Assert.AreEqual(principal, fk.PrincipalEntityType.ClrType);
        Assert.AreEqual(DeleteBehavior.NoAction, fk.DeleteBehavior);
        Assert.IsTrue(fk.Properties.All(p => !p.IsShadowProperty()));
        Assert.AreEqual(dependent != typeof(ApplicationUser), fk.IsRequired);
        if (principal == typeof(Preselection))
        {
            CollectionAssert.AreEqual(new[] { "CampaignId", "CandidateEmployeeId" },
                fk.PrincipalKey.Properties.Select(p => p.Name).ToArray());
        }
    }

    [TestMethod]
    [DataRow(typeof(Employee))]
    [DataRow(typeof(Campaign))]
    [DataRow(typeof(Nomination))]
    [DataRow(typeof(Preselection))]
    [DataRow(typeof(CandidateOpinion))]
    public void Concurrencia_UsaRowversionGeneradaPorSqlServer(Type type)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(type)!.FindProperty("RowVersion")!;
        Assert.IsTrue(property.IsConcurrencyToken);
        Assert.AreEqual(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.AreEqual("rowversion", property.GetColumnType());
        Assert.AreEqual(typeof(byte[]), property.ClrType);
        Assert.IsTrue(property.IsShadowProperty());
    }

    [TestMethod]
    public void Identity_ConservaClavesYRelacionesInternas()
    {
        using var context = CreateContext();
        foreach (var type in new[] { typeof(ApplicationUser), typeof(IdentityRole<Guid>) })
        {
            var key = context.Model.FindEntityType(type)!.FindPrimaryKey()!;
            Assert.AreEqual(typeof(Guid), key.Properties.Single().ClrType);
        }
        var userRoles = context.Model.FindEntityType(typeof(IdentityUserRole<Guid>))!;
        CollectionAssert.AreEqual(new[] { "UserId", "RoleId" }, userRoles.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.AreEqual(2, userRoles.GetForeignKeys().Count());
        Assert.IsTrue(context.Model.FindEntityType(typeof(ApplicationUser))!.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken);
    }

    [TestMethod]
    [DataRow(typeof(IdentityUserLogin<Guid>), 512)]
    [DataRow(typeof(IdentityUserToken<Guid>), 528)]
    public void Identity_ClavesCompuestasRespetanLongitudesYLimiteSqlServer(Type type, int expectedBytes)
    {
        using var context = CreateContext();
        var key = context.Model.FindEntityType(type)!.FindPrimaryKey()!;
        var bytes = 0;
        foreach (var property in key.Properties)
        {
            if (property.ClrType == typeof(string))
            {
                Assert.AreEqual(128, property.GetMaxLength());
                Assert.AreEqual("nvarchar(128)", property.GetColumnType());
                bytes += 256;
            }
            else
            {
                Assert.AreEqual(typeof(Guid), property.ClrType);
                Assert.AreEqual("uniqueidentifier", property.GetColumnType());
                bytes += 16;
            }
        }
        Assert.AreEqual(expectedBytes, bytes);
        Assert.IsTrue(bytes <= 900);
    }

    [TestMethod]
    public void Periodos_SonComplejosEnCampaignsSinEstadoDinamicoPersistido()
    {
        using var context = CreateContext();
        var campaign = context.Model.FindEntityType(typeof(Campaign))!;
        Assert.AreEqual(2, campaign.GetComplexProperties().Count());
        foreach (var name in new[] { "NominationPeriod", "OpinionPeriod" })
        {
            var complex = campaign.FindComplexProperty(name)!;
            Assert.IsFalse(complex.IsNullable);
            CollectionAssert.AreEquivalent(new[] { "Mode", "ScheduledOpeningAtUtc", "ScheduledClosingAtUtc" },
                complex.ComplexType.GetProperties().Select(p => p.Name).ToArray());
        }
        Assert.IsNull(campaign.FindProperty("IsOpen"));
        Assert.AreEqual("date", campaign.FindProperty("EvaluationMonth")!.GetColumnType());
    }

    [TestMethod]
    public void Esquema_RespetaLongitudesYRestricciones()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.AreEqual(32, model.FindEntityType(typeof(Employee))!.FindProperty("PayrollNumber")!.GetMaxLength());
        Assert.AreEqual(200, model.FindEntityType(typeof(Employee))!.FindProperty("FullName")!.GetMaxLength());
        Assert.AreEqual(500, model.FindEntityType(typeof(Nomination))!.FindProperty("Justification")!.GetMaxLength());
        Assert.AreEqual(500, model.FindEntityType(typeof(CandidateOpinion))!.FindProperty("Content")!.GetMaxLength());
        Assert.AreEqual(5, model.FindEntityType(typeof(Campaign))!.GetCheckConstraints().Count());
        Assert.AreEqual(2, model.FindEntityType(typeof(Nomination))!.GetCheckConstraints().Count());
        foreach (var entity in model.GetEntityTypes().Where(e => e.ClrType.Namespace == typeof(Employee).Namespace))
        {
            Assert.IsTrue(entity.GetProperties().Where(p => p.IsShadowProperty()).All(p => p.Name == "RowVersion"));
        }
    }

    [TestMethod]
    public void Modelo_IncluyeTodasLasPropiedadesPublicasDelDominio()
    {
        using var context = CreateContext();
        foreach (var type in new[] { typeof(Employee), typeof(Campaign), typeof(Nomination),
            typeof(Preselection), typeof(CandidateOpinion), typeof(NominationAuditEntry) })
        {
            var entity = context.Model.FindEntityType(type)!;
            CollectionAssert.AreEquivalent(type.GetProperties().Select(p => p.Name).ToArray(),
                entity.GetProperties().Where(p => !p.IsShadowProperty()).Select(p => p.Name)
                    .Concat(entity.GetComplexProperties().Select(p => p.Name)).ToArray());
        }
        Assert.AreEqual("int", context.Model.FindEntityType(typeof(Nomination))!.FindProperty("Qualities")!.GetColumnType());
        Assert.AreEqual("int", context.Model.FindEntityType(typeof(CandidateOpinion))!.FindProperty("OpinionSource")!.GetColumnType());
    }
}
