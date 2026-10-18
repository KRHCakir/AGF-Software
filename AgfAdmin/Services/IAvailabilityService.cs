using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    public interface IAvailabilityService
    {
        Task<ServiceResult<AvailabilityOverviewDto>> GetOverviewAsync(AvailabilityQuery query, CancellationToken cancellationToken = default);
        Task<ServiceResult<AvailabilityDrillDownDto>> GetVariantAsync(int productVariantId, AvailabilityQuery query, CancellationToken cancellationToken = default);
    }
}
