using System.Data;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Assignments;
using EmpleadoDelMes.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class AssignmentRepository(ApplicationDbContext context, TimeProvider clock) : IAssignmentRepository
{
    private IQueryable<Employee> Sponsors() => context.Employees.AsNoTracking().Where(e =>
        context.Users.Any(u => u.EmployeeId == e.Id && u.IsActive && context.UserRoles.Any(ur => ur.UserId == u.Id &&
            context.Roles.Any(r => r.Id == ur.RoleId && r.Name == AccountRoles.Sponsor))));
    private IQueryable<SponsorEmployeeAssignment> Active() => context.SponsorEmployeeAssignments.AsNoTracking().Where(a => a.IsActive);
    private static AssignmentDto Dto(SponsorEmployeeAssignment a, byte[] v) =>
        new(a.Id, a.SponsorEmployeeId, a.EmployeeId, a.IsActive, a.CreatedAtUtc, a.UpdatedAtUtc, Convert.ToBase64String(v));

    public async Task<AssignmentResult<AssignmentPage>> SearchAsync(AssignmentSearch s, CancellationToken token)
    {
        try
        {
            if (s.SponsorId is { } sponsor && !await context.Employees.AnyAsync(e => e.Id == sponsor, token))
                return new(null, AssignmentError.NotFound, "No encontramos al patrocinador.");
            var query = s.Kind == AssignmentSearchKind.Sponsors ? Sponsors() : context.Employees.AsNoTracking();
            if (s.Kind == AssignmentSearchKind.Collaborators)
                query = query.Where(e => context.SponsorEmployeeAssignments.Any(a => a.IsActive && a.EmployeeId == e.Id && a.SponsorEmployeeId == s.SponsorId));
            if (s.Kind is AssignmentSearchKind.Available or AssignmentSearchKind.Pending)
                query = query.Where(e => !context.SponsorEmployeeAssignments.Any(a => a.EmployeeId == e.Id && a.IsActive));
            if (s.Kind == AssignmentSearchKind.Available && s.SponsorId.HasValue) query = query.Where(e => e.Id != s.SponsorId);
            if (!string.IsNullOrWhiteSpace(s.Term)) query = query.Where(e => e.PayrollNumber.Contains(s.Term) || e.FullName.Contains(s.Term));
            var total = await query.CountAsync(token);
            var items = await query.OrderBy(e => e.PayrollNumber).ThenBy(e => e.Id).Skip((s.PageNumber - 1) * s.PageSize).Take(s.PageSize)
                .Select(e => new AssignmentEmployee(e.Id, e.PayrollNumber, e.FullName)).ToArrayAsync(token);
            return new(new(items, total, s.PageNumber, s.PageSize));
        }
        catch (SqlException) { throw new AssignmentPersistenceException(AssignmentError.Persistence); }
    }

    public async Task<AssignmentResult<AssignmentDto>> CurrentAsync(Guid employeeId, CancellationToken token)
    {
        try
        {
            if (!await context.Employees.AnyAsync(e => e.Id == employeeId, token)) return new(null, AssignmentError.NotFound, "No encontramos al empleado.");
            var row = await Active().Where(a => a.EmployeeId == employeeId)
                .Select(a => new { Entity = a, Version = EF.Property<byte[]>(a, "RowVersion") }).SingleOrDefaultAsync(token);
            return new(row is null ? null : Dto(row.Entity, row.Version));
        }
        catch (SqlException) { throw new AssignmentPersistenceException(AssignmentError.Persistence); }
    }

    public async Task<AssignmentResult<AssignmentDto>> ChangeAsync(Guid employeeId, Guid? sponsorId, AssignmentChange change, byte[]? version, CancellationToken token)
    {
        SponsorEmployeeAssignment? old = null, created = null;
        try
        {
            // Serialize all graph mutations across instances; Serializable also stabilizes Identity eligibility reads.
            await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = tx.GetDbTransaction();
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'EmpleadoDelMes.SponsorAssignments.v1', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000; SELECT @r;";
            if ((int)(await command.ExecuteScalarAsync(token))! < 0) throw new AssignmentPersistenceException(AssignmentError.Conflict);
            if (!await context.Employees.AnyAsync(e => e.Id == employeeId, token)) return new(null, AssignmentError.NotFound, "No encontramos al empleado.");
            if (change != AssignmentChange.Withdraw)
            {
                if (!await context.Employees.AnyAsync(e => e.Id == sponsorId, token)) return new(null, AssignmentError.NotFound, "No encontramos al patrocinador.");
                if (!await Sponsors().AnyAsync(e => e.Id == sponsorId, token)) return new(null, AssignmentError.Validation, "El patrocinador debe tener una cuenta activa con rol Sponsor.");
            }
            var row = await Active().Where(a => a.EmployeeId == employeeId)
                .Select(a => new { Entity = a, Version = EF.Property<byte[]>(a, "RowVersion") }).SingleOrDefaultAsync(token);
            if (change == AssignmentChange.Assign && row is not null) return new(null, AssignmentError.Conflict, "El empleado ya tiene un patrocinador asignado.");
            if (change != AssignmentChange.Assign && row is null) return new(null, AssignmentError.NotFound, "El empleado no tiene una asignación vigente.");
            if (row is not null && (version is null || !row.Version.SequenceEqual(version))) throw new AssignmentPersistenceException(AssignmentError.Conflict);
            if (change == AssignmentChange.Reassign && row!.Entity.SponsorEmployeeId == sponsorId)
                return new(null, AssignmentError.Conflict, "El empleado ya está asignado a ese patrocinador.");
            if (change != AssignmentChange.Withdraw)
            {
                // Follow only the sponsor's active ancestor chain. Inactive history does not form graph edges.
                var visited = new HashSet<Guid> { employeeId };
                Guid? cursor = sponsorId;
                while (cursor.HasValue)
                {
                    token.ThrowIfCancellationRequested();
                    if (!visited.Add(cursor.Value)) return new(null, AssignmentError.Conflict, "La asignación produciría una relación circular entre patrocinadores.");
                    var current = cursor.Value;
                    cursor = await Active().Where(a => a.EmployeeId == current).Select(a => (Guid?)a.SponsorEmployeeId).SingleOrDefaultAsync(token);
                }
            }
            var now = clock.GetUtcNow();
            if (row is not null)
            {
                old = row.Entity;
                if (now < old.CreatedAtUtc) return new(null, AssignmentError.Conflict, "No se puede registrar el cambio con la hora actual. Inténtalo nuevamente.");
                var entry = context.Attach(old);
                entry.Property<byte[]>("RowVersion").OriginalValue = version!;
                entry.Property<byte[]>("RowVersion").CurrentValue = row.Version;
                old.Withdraw(now);
                // Release the filtered unique key before INSERT, within the same transaction.
                await context.SaveChangesAsync(token);
            }
            if (change != AssignmentChange.Withdraw)
            {
                created = new(Guid.NewGuid(), sponsorId!.Value, employeeId, now);
                context.SponsorEmployeeAssignments.Add(created);
                await context.SaveChangesAsync(token);
            }
            var entity = created ?? old!;
            var dto = Dto(entity, context.Entry(entity).Property<byte[]>("RowVersion").CurrentValue!);
            await tx.CommitAsync(token);
            return new(dto);
        }
        catch (DbUpdateConcurrencyException) { throw new AssignmentPersistenceException(AssignmentError.Conflict); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 1222 })
        { throw new AssignmentPersistenceException(AssignmentError.Conflict); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627) { throw new AssignmentPersistenceException(AssignmentError.Conflict); }
        catch (DbUpdateException) { throw new AssignmentPersistenceException(AssignmentError.Persistence); }
        catch (SqlException) { throw new AssignmentPersistenceException(AssignmentError.Persistence); }
        finally
        {
            if (old is not null) context.Entry(old).State = EntityState.Detached;
            if (created is not null) context.Entry(created).State = EntityState.Detached;
        }
    }
}
