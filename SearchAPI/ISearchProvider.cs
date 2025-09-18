namespace LetheAISharp.SearchAPI
{
    // Common interface for search providers
    public interface ISearchProvider
    {
        Task<List<SearchResult>> SearchAsync(string query, int maxResults);
        string ProviderName { get; }
    }
}


