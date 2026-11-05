namespace SimCore
{
    /// <summary>What a player's account keeps on the server: the four progress texts (see <see cref="CampaignProgress"/>, <see cref="Armory"/>, <see cref="DailyProgress"/>).</summary>
    public sealed class ProfileData
    {
        public string Campaign = "";
        public string Armory = "";
        public string Daily = "";
        public string Tutorial = "";

        public bool SameAs(ProfileData other) => Campaign == other.Campaign && Armory == other.Armory && Daily == other.Daily && Tutorial == other.Tutorial;
    }

    public static class ProfileMerge
    {
        /// <summary>
        /// Joins the progress kept on this phone with the progress on the server, so signing in on a second device or after a reinstall
        /// loses nothing. Neither side is thrown away: the best of both is kept. (Until the server checks results itself, the wallet
        /// is the larger of the two; see ACCOUNTS_PLAN.md.)
        /// </summary>
        public static ProfileData Merge(ProfileData local, ProfileData server)
        {
            CampaignProgress campaign = CampaignProgress.Parse(local.Campaign);
            campaign.MergeFrom(CampaignProgress.Parse(server.Campaign));
            Armory armory = Armory.Parse(local.Armory);
            armory.MergeFrom(Armory.Parse(server.Armory));
            DailyProgress daily = DailyProgress.Parse(local.Daily);
            daily.MergeFrom(DailyProgress.Parse(server.Daily));
            return new ProfileData
            {
                Campaign = campaign.Serialize(),
                Armory = armory.Serialize(),
                Daily = daily.Serialize(),
                Tutorial = local.Tutorial == "1" || server.Tutorial == "1" ? "1" : "",
            };
        }
    }
}
