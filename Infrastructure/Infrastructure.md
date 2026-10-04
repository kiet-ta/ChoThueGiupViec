# Caching services:
### This folder contains implementations for caching mechanisms used in the application. 
- InMemoryCacheService.cs: Implements caching using in-memory storage for fast access to frequently used data. 
  - Example:
```csharp
public async Task<UserProfile?> GetUserProfileAsync(int userId)
{
    var cacheKey = $"User:{userId}:Profile";

    var cachedProfile = await _cacheService.GetAsync<UserProfile>(cacheKey);
    if (cachedProfile != null)
        return cachedProfile;

    // Nếu không có trong cache → query DB
    var profile = await _dbContext.UserProfiles.FindAsync(userId);
    if (profile != null)
    {
        await _cacheService.SetAsync(cacheKey, profile, TimeSpan.FromMinutes(10));
    }
    return profile;
}
```
  - [Sequence Flow](https://mermaid.live/edit#pako:eNqVUktrwzAM_itGpw7S0jRZEvtQWFsYOwy2lV1GLiZRm7DEzhy7tCv973MeTTfIZT7J0veQhM6QyBSBQY1fBkWCm5zvFS9jQexbS6GVLApUZLpcki2qQ54gI4-o32tUL0ru8gInxsZP6V3H6UEtYc2TrIM_1CeRTD7x1KN4obsqyXLdpVrDNjX9Y_aG2ihBkqaUkgMvDN4Ig1trN7Q7sBTWpugNsKixdyjzuh4RsRqbFSOvBtWJNFORqhvxht2sxtsbR48sY_trGU43jkPwWOXqupt_zyXSWIADe5WnwLSyglCiKnnzhXMDikFnWGIMzIYp7nhDhlhcLK3i4kPK8spU0uwzYDtut-WAqVKur0cxQKwjqrU0QgNz3XmrAewMR_uNopnneUEQ0YgGi_t56MAJGHVnIV14lPq-H4QhdS8OfLeu81kYuP7cd6nnuQF1AwcwzbVUz91htvd5-QHBpNRk)