namespace SmartHydroAPI.Models.Dtos
{
    // Generic paginated response contract
    public record PagedResultDto<T>(
        IReadOnlyList<T> Items,
        int TotalCount,
        int Page,
        int PageSize,
        int TotalPages
    )
    {
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }
}
