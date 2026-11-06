using System.Security.Claims;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public static class SiteAccess
    {
        // Returns the caller's email trimmed and lower-cased, or an empty string when the token has none
        public static string NormalizedEmail(ClaimsPrincipal user)
        {
            var email = user.GetEmail();

            if (string.IsNullOrWhiteSpace(email))
                return string.Empty;

            return email.Trim().ToLowerInvariant();
        }

        // Builds the ids of the active sites the caller may read: every site for an Admin, assigned sites for a Worker, owned sites for an Operator
        public static IQueryable<Guid> VisibleSiteIds(ApplicationDbContext database, ClaimsPrincipal user)
        {
            var sites = database.Sites.Where(s => !s.IsDeleted);

            if (user.IsAdmin())
                return sites.Select(s => s.Id);

            var email = NormalizedEmail(user);

            if (user.IsInRole(UserRoles.Worker))
            {
                var assignedSiteIds = database.SiteUsers
                    .Where(su => database.Users.Any(u => u.Id == su.UserId && u.Email == email))
                    .Select(su => su.SiteId);

                return sites.Where(s => assignedSiteIds.Contains(s.Id)).Select(s => s.Id);
            }

            return OwnedSites(sites, email).Select(s => s.Id);
        }

        // Builds the ids of the active sites the caller may change: every site for an Admin, owned sites for an Operator and none for a Worker
        public static IQueryable<Guid> ManageableSiteIds(ApplicationDbContext database, ClaimsPrincipal user)
        {
            var sites = database.Sites.Where(s => !s.IsDeleted);

            if (user.IsAdmin())
                return sites.Select(s => s.Id);

            if (!user.IsInRole(UserRoles.Operator))
                return sites.Where(s => false).Select(s => s.Id);

            return OwnedSites(sites, NormalizedEmail(user)).Select(s => s.Id);
        }

        // Builds the deleted sites the caller may restore: every deleted site for an Admin, the deleted sites they owned for an Operator and none for a Worker
        public static IQueryable<Site> ManageableDeletedSites(ApplicationDbContext database, ClaimsPrincipal user)
        {
            var deleted = database.Sites.Where(s => s.IsDeleted);

            if (user.IsAdmin())
                return deleted;

            if (!user.IsInRole(UserRoles.Operator))
                return deleted.Where(s => false);

            return OwnedSites(deleted, NormalizedEmail(user));
        }

        // Builds the structures the caller may read, following the visibility of the sites they belong to
        public static IQueryable<Structure> VisibleStructures(ApplicationDbContext database, ClaimsPrincipal user)
        {
            var structures = database.Structures.AsQueryable();

            if (user.IsAdmin())
                return structures;

            var siteIds = VisibleSiteIds(database, user);
            var email = NormalizedEmail(user);
            var isOperator = user.IsInRole(UserRoles.Operator);

            return structures.Where(s =>
                (s.SiteId != null && siteIds.Contains(s.SiteId.Value))
                || (isOperator && s.SiteId == null && s.OwnerEmail != null && s.OwnerEmail.ToLower() == email));
        }

        // Builds the structures the caller may change: every structure for an Admin, the structures in owned sites for an Operator, none for a Worker
        public static IQueryable<Structure> ManageableStructures(ApplicationDbContext database, ClaimsPrincipal user)
        {
            var structures = database.Structures.AsQueryable();

            if (user.IsAdmin())
                return structures;

            var siteIds = ManageableSiteIds(database, user);
            var email = NormalizedEmail(user);
            var isOperator = user.IsInRole(UserRoles.Operator);

            return structures.Where(s =>
                isOperator
                && ((s.SiteId != null && siteIds.Contains(s.SiteId.Value))
                    || (s.SiteId == null && s.OwnerEmail != null && s.OwnerEmail.ToLower() == email)));
        }

        // Builds the ids of the active structures the caller may read, used to scope alerts and photos
        public static IQueryable<int> VisibleStructureIds(ApplicationDbContext database, ClaimsPrincipal user)
        {
            return VisibleStructures(database, user).Where(s => !s.IsDeleted).Select(s => s.Id);
        }

        // Builds the ids of the structures the caller may change, including deleted ones so their items can still be restored
        public static IQueryable<int> ManageableStructureIds(ApplicationDbContext database, ClaimsPrincipal user)
        {
            return ManageableStructures(database, user).Select(s => s.Id);
        }

        // Filters a site query down to the sites owned by the given lower-cased email
        public static IQueryable<Site> OwnedSites(IQueryable<Site> sites, string email)
        {
            return sites.Where(s => s.OwnerEmail != null && s.OwnerEmail.ToLower() == email);
        }
    }
}