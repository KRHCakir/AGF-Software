using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    public interface IPalletService
    {
        Task<ServiceResult<PalletDto>> CreateAsync(CreatePalletDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<PalletContentLineDto>> AddContentLineAsync(int palletId, AddPalletContentLineDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<PalletDto>> GetAsync(int id, CancellationToken cancellationToken = default);
        Task<ServiceResult<PalletDto>> ChangeLogisticsStatusAsync(int palletId, ChangeLogisticsStatusDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<PalletDto>> ChangeLocationAsync(int palletId, ChangePalletLocationDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<PalletContentLineDto>> ChangeQualityStatusAsync(int palletId, int contentLineId, ChangeQualityStatusDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<PalletAuditEventDto>>> GetAuditEventsAsync(int palletId, CancellationToken cancellationToken = default);
    }
}
