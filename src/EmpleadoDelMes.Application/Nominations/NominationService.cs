using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Application.Nominations;

public sealed class NominationService(INominationRepository repository, EmployeeService employees, TimeProvider clock)
{
    public Task<NominationResult<ActiveNominationCampaign>> GetActiveCampaignAsync(Guid userId, CancellationToken token = default) =>
        Execute<ActiveNominationCampaign>(async () =>
        {
            var denied = ValidateActor(await repository.GetActorAsync(userId, token));
            if (denied is not null) return new(null, denied.Error, denied.Message);
            var active = SelectCampaign(await repository.GetPotentialCampaignsAsync(token), clock.GetUtcNow());
            return active.IsSuccess ? new(new(active.Value!.Id, active.Value.EvaluationMonth)) : new(null, active.Error, active.Message);
        });

    public Task<NominationResult<CandidatePage>> SearchCandidatesAsync(Guid userId, string? term, int pageNumber = 1,
        int pageSize = 20, CancellationToken token = default) => Execute<CandidatePage>(async () =>
    {
        var denied = ValidateActor(await repository.GetActorAsync(userId, token));
        if (denied is not null) return new(null, denied.Error, denied.Message);
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length > 100)
            return new(null, NominationError.Validation, "Escribe una nómina o un nombre para buscar, de hasta 100 caracteres.");
        var result = await employees.SearchEmployees(term, pageNumber, pageSize, token);
        if (!result.IsSuccess) return new(null, result.Error == EmployeeError.Validation ? NominationError.Validation : NominationError.Persistence, result.Message);
        var page = result.Value!;
        return new(new(page.Items.Select(e => new CandidateDto(e.Id, e.PayrollNumber, e.FullName)).ToArray(), page.Total, page.PageNumber, page.PageSize));
    });

    public Task<NominationResult<NominationConfirmation>> CreateAsync(Guid userId, Guid candidateId,
        NominationQualities qualities, string? justification, CancellationToken token = default)
    {
        if (candidateId == Guid.Empty) return Task.FromResult(new NominationResult<NominationConfirmation>(null,
            NominationError.Validation, "Selecciona un empleado válido."));
        return Execute(() => repository.CreateAsync(userId, candidateId, context =>
        {
            var denied = ValidateActor(context.Actor);
            if (denied is not null) return new(null, denied.Error, denied.Message);
            if (context.Candidate is null) return new(null, NominationError.NotFound, "No encontramos al empleado postulado.");
            var now = clock.GetUtcNow();
            var active = SelectCampaign(context.Campaigns, now);
            if (!active.IsSuccess) return new(null, active.Error, active.Message);
            try
            {
                return new(new Nomination(Guid.NewGuid(), active.Value!.Id, context.Actor!.Employee!, context.Candidate,
                    context.Actor.UserId, qualities, justification!, now));
            }
            catch (ArgumentException ex)
            {
                return new(null, NominationError.Validation, ex.ParamName switch
                {
                    "candidate" => "No puedes postularte a ti mismo.",
                    "qualities" => "Selecciona al menos una de las cuatro cualidades disponibles.",
                    "justification" => "Escribe una justificación de máximo 500 caracteres; no puede estar vacía.",
                    _ => "Revisa los datos de la postulación."
                });
            }
        }, token));
    }

    private static NominationResult<bool>? ValidateActor(NominationActor? actor)
    {
        if (actor is null || !actor.IsActive || actor.IsLockedOut)
            return new(false, NominationError.Unauthorized, "Tu sesión no es válida. Inicia sesión nuevamente.");
        if (!actor.IsSponsor) return new(false, NominationError.Forbidden, "No tienes permiso para registrar postulaciones.");
        if (actor.MustChangePassword) return new(false, NominationError.Forbidden, "Debes cambiar tu contraseña antes de continuar.");
        if (actor.Employee is null) return new(false, NominationError.Forbidden, "Tu cuenta no está vinculada a un empleado válido. Contacta a administración.");
        return null;
    }

    private static NominationResult<Campaign> SelectCampaign(IReadOnlyList<Campaign> campaigns, DateTimeOffset now)
    {
        var active = campaigns.Where(c => c.CanAcceptNominations(now)).Take(2).ToArray();
        return active.Length switch
        {
            0 => new(null, NominationError.NoActiveCampaign, "No hay una convocatoria que acepte postulaciones en este momento."),
            1 => new(active[0]),
            _ => new(null, NominationError.Concurrency, "No pudimos determinar una única convocatoria abierta. Contacta a administración.")
        };
    }

    private static async Task<NominationResult<T>> Execute<T>(Func<Task<NominationResult<T>>> operation)
    {
        try { return await operation(); }
        catch (NominationPersistenceException ex)
        {
            return new(default, ex.Error, ex.Error switch
            {
                NominationError.Duplicate => "Ya postulaste a este empleado en esta convocatoria, incluso si la postulación fue anulada.",
                NominationError.Concurrency => "La información cambió durante la operación. Inténtalo nuevamente.",
                _ => "No pudimos completar la operación. Inténtalo nuevamente."
            });
        }
    }
}
