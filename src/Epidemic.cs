//  Authors:  Robert M. Scheller

using Landis.Core;
using Landis.Library.BiomassCohorts;
using Landis.SpatialModeling;
using System.Collections.Generic;

namespace Landis.Extension.BiomassBDA
{
    public class Epidemic
        : IDisturbance

    {
        private static IEcoregionDataset ecoregions;

        private IAgent epidemicParms;
        private int siteSeverity;
        private double random;
        private double siteVulnerability;
        private double totalSiteBiomass;

        public int TotalCohortsKilled;
        public int CohortsKilled;
        public double MeanSeverity;
        public int TotalSitesDamaged;
        public int TotalBiomassMortality;




        private ActiveSite currentSite; // current site where cohorts are being damaged

        private enum TempPattern        {random, cyclic};
        private enum NeighborShape      {uniform, linear, gaussian};
        private enum InitialCondition   {map, none};
        private enum SRDMode            {SRDmax, SRDmean};


        //---------------------------------------------------------------------

        static Epidemic()
        {
        }

        //---------------------------------------------------------------------

        ExtensionType IDisturbance.Type
        {
            get {
                return PlugIn.type;
            }
        }

        //---------------------------------------------------------------------

        ActiveSite IDisturbance.CurrentSite
        {
            get {
                return currentSite;
            }
        }
        //---------------------------------------------------------------------

        IAgent EpidemicParameters
        {
            get
            {
                return epidemicParms;
            }
        }

        //---------------------------------------------------------------------
        ///<summary>
        ///Initialize an Epidemic - defined as an agent outbreak for an entire landscape
        ///at a single BDA timestep.  One epidemic per agent per BDA timestep
        ///</summary>

        public static void Initialize(IAgent agent)
        {
            PlugIn.ModelCore.UI.WriteLine("      Initializing {0}.", agent.AgentName);

            ecoregions = PlugIn.ModelCore.Ecoregions;


            //.ActiveSiteValues allows you to reset all active site at once.
            SiteVars.NeighborResourceDom.ActiveSiteValues = 0;
            SiteVars.Vulnerability.ActiveSiteValues = 0;
            SiteVars.SiteResourceDomMod.ActiveSiteValues = 0;
            SiteVars.SiteResourceDom.ActiveSiteValues = 0;

            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                if(agent.OutbreakZone[site] == Zone.Newzone)
                    agent.OutbreakZone[site] = Zone.Lastzone;
                else
                    agent.OutbreakZone[site] = Zone.Nozone;
            }

        }

        //---------------------------------------------------------------------
        ///<summary>
        ///Simulate an Epidemic - This is the controlling function that calls the
        ///subsequent function.  The basic logic of an epidemic resides here.
        ///</summary>
        public static Epidemic Simulate(IAgent agent,
                                        int currentTime,
                                        int timestep,
                                        int ROS)
        {


            PlugIn.ModelCore.UI.WriteLine("      {0}: Epidemic Activated (Simulate).", agent.AgentName);
            Epidemic CurrentEpidemic = new Epidemic(agent);

            //SiteResources.SiteResourceDominance(agent, ROS, SiteVars.Cohorts);
            SiteResources.SiteResourceDominance(agent, ROS);
            SiteResources.SiteResourceDominanceModifier(agent);

            if(agent.Dispersal) {
                //Asynchronous - Simulate Agent Dispersal

                // Calculate Site Vulnerability without considering the Neighborhood
                // If neither disturbance modifiers nor ecoregion modifiers are active,
                //  Vulnerability will equal SiteReourceDominance.
                SiteResources.SiteVulnerability(agent, ROS, false);

                Epicenters.NewEpicenters(agent, timestep);

            } else
            {
                //Synchronous:  assume that all Active sites can potentially be
                //disturbed without regard to initial locations.
                foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
                    agent.OutbreakZone[site] = Zone.Newzone;

            }

            //Consider the Neighborhood if requested:
            if (agent.NeighborFlag)
                SiteResources.NeighborResourceDominance(agent);

            //Recalculate Site Vulnerability considering neighbors if necessary:
            SiteResources.SiteVulnerability(agent, ROS, agent.NeighborFlag);

            CurrentEpidemic.DisturbSites(agent);

            return CurrentEpidemic;
        }

        //---------------------------------------------------------------------
        // Epidemic Constructor
        private Epidemic(IAgent agent)
        {
            this.epidemicParms = agent;
            this.TotalCohortsKilled = 0;
            this.MeanSeverity = 0.0;
            this.TotalSitesDamaged = 0;
            this.TotalBiomassMortality = 0; 
        }

        //---------------------------------------------------------------------
        //Go through all active sites and damage them according to the
        //Site Vulnerability.
        private void DisturbSites(IAgent agent)
        {
            PlugIn.ModelCore.UI.WriteLine("      {0}: Disturb Sites.", agent.AgentName); 
            int totalSiteSeverity = 0;
            int cohortsKilled = 0;

            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                totalSiteBiomass = (double)SiteVars.TotalSiteBiomass(site);
                this.siteSeverity = 0;
                this.random = 0;
                this.currentSite = site;

                double myRand = PlugIn.ModelCore.GenerateUniform();
                if(PlugIn.CalibrateMode)
                    PlugIn.ModelCore.UI.WriteLine("RANDOM NUMBER = {0}", myRand);

                if (agent.OutbreakZone[site] == Zone.Newzone
                    && SiteVars.Vulnerability[site] > myRand)
                {
                    //PlugIn.ModelCore.Log.WriteLine("Zone={0}, agent.OutbreakZone={1}", Zone.Newzone.ToString(), agent.OutbreakZone[site]);
                    //PlugIn.ModelCore.Log.WriteLine("Vulnerability={0}, Randnum={1}", SiteVars.Vulnerability[site], PlugIn.ModelCore.GenerateUniform());
                    double vulnerability = SiteVars.Vulnerability[site];

                    if(vulnerability >= 0) this.siteSeverity= 1;

                    if(vulnerability >= agent.Class2_SV) this.siteSeverity= 2;

                    if(vulnerability >= agent.Class3_SV) this.siteSeverity= 3;

                    this.random = myRand;
                    this.siteVulnerability = SiteVars.Vulnerability[site];

                    if (this.siteSeverity > 0)
                        cohortsKilled = Damage(site); 

                    if (cohortsKilled > 0)
                    {
                        this.TotalCohortsKilled += cohortsKilled;
                        this.TotalSitesDamaged++;
                        totalSiteSeverity += this.siteSeverity;
                        SiteVars.Disturbed[site] = true;
                        SiteVars.TimeOfLastEvent[site] = PlugIn.ModelCore.CurrentTime;
                        SiteVars.AgentName[site] = agent.AgentName;
                    } else
                        this.siteSeverity = 0;
                }
                agent.Severity[site] = (byte) this.siteSeverity;
            }
            if (this.TotalSitesDamaged > 0)
                this.MeanSeverity = (double) totalSiteSeverity / (double) this.TotalSitesDamaged;
        }

        //---------------------------------------------------------------------
        //A small helper function for going through list of cohorts at a site
        //and checking them with the filter provided by RemoveMarkedCohort(ICohort).
        private int Damage(ActiveSite site)
        {
            int previousCohortsKilled = this.CohortsKilled;
            SiteVars.Cohorts[site].ReduceOrKillBiomassCohorts(this);
            if (PlugIn.CalibrateMode)
                PlugIn.ModelCore.UI.WriteLine("      CohortsKilled={0}, PreviousCohortsKilled={1}", this.CohortsKilled, previousCohortsKilled);
            return this.CohortsKilled - previousCohortsKilled;

        }

        //---------------------------------------------------------------------
        // DamageCohort is a filter to determine which cohorts are removed.
        // Each cohort is passed into the function and tested whether it should
        // be killed.
        int IDisturbance.ReduceOrKillMarkedCohort(ICohort cohort)
        {
            
            bool killCohort = false;

            ISppParameters sppParms = epidemicParms.SppParameters[cohort.Species.Index];

            if (cohort.Age >= sppParms.ResistantHostAge && cohort.Age < sppParms.TolerantHostAge)
            {
                if (this.random <= this.siteVulnerability * sppParms.ResistantHostVuln)
                {
                    if (PlugIn.CalibrateMode)
                        PlugIn.ModelCore.UI.WriteLine("      Resistant Host Age: Cohort={0}, {1}, {2}. SiteVuln={3}", cohort.Species.Name, cohort.Age, sppParms.ResistantHostVuln, this.siteVulnerability);
                    killCohort = true;
                }
            }

            if (cohort.Age >= sppParms.TolerantHostAge && cohort.Age < sppParms.VulnerableHostAge)
            {
                if (this.random <= this.siteVulnerability * sppParms.TolerantHostVuln)
                {
                    if (PlugIn.CalibrateMode)
                        PlugIn.ModelCore.UI.WriteLine("      Tolerant Host Age: Cohort={0}, {1}, {2}. SiteVuln={3}", cohort.Species.Name, cohort.Age, sppParms.TolerantHostVuln, this.siteVulnerability);
                    killCohort = true;
                }
            }

            if (cohort.Age >= sppParms.VulnerableHostAge)
            {
                if (this.random <= this.siteVulnerability * sppParms.VulnerableHostVuln)
                {
                    if (PlugIn.CalibrateMode)
                        PlugIn.ModelCore.UI.WriteLine("      Vulnerable Host Age: Cohort={0}, {1}, {2}. SiteVuln={3}", cohort.Species.Name, cohort.Age, sppParms.VulnerableHostVuln, this.siteVulnerability);
                    killCohort = true;
                }
            }
            

            if (killCohort)
            {
                if (PlugIn.CalibrateMode)
                    PlugIn.ModelCore.UI.WriteLine("      Damage Cohort={0}, {1}, {2}.", cohort.Species.Name, cohort.Age, cohort.Biomass);
                this.CohortsKilled++;
                this.TotalBiomassMortality += cohort.Biomass;
                return cohort.Biomass;
            }

            return 0;
        }

    }

}

