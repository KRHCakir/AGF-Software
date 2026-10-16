using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    public interface ISupplyCommitmentService
    {
        Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<SupplyCommitmentDto>> GetAsync(int id, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<SupplyCommitmentDto>>> ListAsync(SupplyCommitmentFilter filter, CancellationToken cancellationToken = default);
    }
}
