using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    public interface IPalletService
    {
        Task<ServiceResult<PalletDto>> CreateAsync(CreatePalletDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<PalletContentLineDto>> AddContentLineAsync(int palletId, AddPalletContentLineDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<PalletDto>> GetAsync(int id, CancellationToken cancellationToken = default);
    }
}
