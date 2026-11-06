namespace Group_1_SmartHydro_MVC.Patterns.Observer
{
    public enum SiteChangeKind
    {
        SiteCreated,
        SiteUpdated,
        SiteOwnerChanged,
        SiteDeleted,
        SiteRestored,
        SitePurged,
        StructureAdded,
        StructureUpdated,
        StructureDeleted,
        StructureRestored,
        StructurePurged,
        OperatorReviewed,
        AccountUpdated,
        WorkerRemoved
    }

    public sealed record SiteChange(string SiteId, SiteChangeKind Kind, string Subject, string ActorEmail)
    {
        private static readonly Dictionary<SiteChangeKind, string> Templates = new()
        {
            [SiteChangeKind.SiteCreated] = "Site \"{0}\" was created",
            [SiteChangeKind.SiteUpdated] = "Site \"{0}\" was updated",
            [SiteChangeKind.SiteOwnerChanged] = "Site \"{0}\" has a new owner",
            [SiteChangeKind.SiteDeleted] = "Site \"{0}\" was deleted",
            [SiteChangeKind.SiteRestored] = "Site \"{0}\" was restored",
            [SiteChangeKind.SitePurged] = "Site \"{0}\" was permanently deleted",
            [SiteChangeKind.StructureAdded] = "\"{0}\" was added",
            [SiteChangeKind.StructureUpdated] = "\"{0}\" was updated",
            [SiteChangeKind.StructureDeleted] = "\"{0}\" was deleted",
            [SiteChangeKind.StructureRestored] = "\"{0}\" was restored",
            [SiteChangeKind.StructurePurged] = "\"{0}\" was permanently deleted",
            [SiteChangeKind.OperatorReviewed] = "Operator {0} was reviewed",
            [SiteChangeKind.AccountUpdated] = "Account {0} was updated",
            [SiteChangeKind.WorkerRemoved] = "{0} was removed from this site"
        };

        // Builds the one-line description shown to people watching the site
        public string Describe()
        {
            var text = string.Format(Templates[Kind], Subject);

            if (string.IsNullOrWhiteSpace(ActorEmail))
                return text + ".";

            return text + " by " + ActorEmail + ".";
        }
    }
}