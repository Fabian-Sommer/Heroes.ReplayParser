﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Heroes.ReplayParser
{
    public static class Statistics
    {
        public static void Parse(Replay replay)
        {
            // I believe these 'PlayerID' are just indexes to the ClientList, but we should use the info given in this file just to be safe
            var playerIDDictionary = new Dictionary<int, Player>();
            
            for (var i = 0; i < replay.TeamLevels.Length; i++)
            {
                replay.TeamLevels[i] = new Dictionary<int, TimeSpan>();
                replay.TeamPeriodicXPBreakdown[i] = new List<PeriodicXPBreakdown>();
            }

            foreach (var firstCatapultPerTeam in replay.Units.Where(i => i.Name == "CatapultMinion" && i.Team.HasValue).GroupBy(i => i.Team.Value).Select(i => i.OrderBy(j => j.TimeSpanBorn).First()))
                replay.TeamObjectives[firstCatapultPerTeam.Team.Value].Add(new TeamObjective {
                    TimeSpan = firstCatapultPerTeam.TimeSpanBorn,
                    TeamObjectiveType = TeamObjectiveType.FirstCatapultSpawn,
                    Value = -1 });

            foreach (var vehicleUnit in replay.Units.Where(i => (i.Name == "VehiclePlantHorror" || i.Name == "VehicleDragon") && (i.PlayerControlledBy != null || i.OwnerChangeEvents.Any(j => j.PlayerNewOwner != null))))
            {
                var ownerChangeEvent = vehicleUnit.OwnerChangeEvents.SingleOrDefault(i => i.PlayerNewOwner != null);

                replay.TeamObjectives[(ownerChangeEvent != null ? ownerChangeEvent.PlayerNewOwner : vehicleUnit.PlayerControlledBy).Team].Add(new TeamObjective {
                    Player = ownerChangeEvent != null ? ownerChangeEvent.PlayerNewOwner : vehicleUnit.PlayerControlledBy,
                    TimeSpan = ownerChangeEvent != null ? ownerChangeEvent.TimeSpanOwnerChanged : vehicleUnit.TimeSpanAcquired.Value,
                    TeamObjectiveType = vehicleUnit.Name == "VehiclePlantHorror" ? TeamObjectiveType.GardenOfTerrorGardenTerrorActivatedWithGardenTerrorDurationSeconds : TeamObjectiveType.DragonShireDragonKnightActivatedWithDragonDurationSeconds,
                    Value = (int) ((vehicleUnit.TimeSpanDied ?? replay.ReplayLength) - (ownerChangeEvent != null ? ownerChangeEvent.TimeSpanOwnerChanged : vehicleUnit.TimeSpanAcquired.Value)).TotalSeconds });
            }

            var playerIDTalentIndexDictionary = new Dictionary<int, int>();

            foreach (var trackerEvent in replay.TrackerEvents.Where(i => i.TrackerEventType == ReplayTrackerEvents.TrackerEventType.UpgradeEvent || i.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent || i.TrackerEventType == ReplayTrackerEvents.TrackerEventType.ScoreResultEvent))
                switch (trackerEvent.TrackerEventType)
                {
                    case ReplayTrackerEvents.TrackerEventType.UpgradeEvent:
                        switch (trackerEvent.Data.dictionary[1].blobText)
                        {
                            case "CreepColor":
                                // Not sure what this is - it's been in the replay file since Alpha, so it may just be a SC2 remnant
                                break;

                            case "IsPlayer11":
                            case "IsPlayer12":
                                // Also not sure what this is
                                break;

                            case "GatesAreOpen":
                            case "MinionsAreSpawning":
                            case "GallTalentNetherCallsUpgrade":
                            case "TracerJumperButtonSwap":
                                // Not really interested in these
                                break;

                            case "VehicleDragonUpgrade":
                                break;

                            case "NovaSnipeMasterDamageUpgrade":
                                playerIDDictionary[(int) trackerEvent.Data.dictionary[0].vInt.Value].UpgradeEvents.Add(new UpgradeEvent {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    UpgradeEventType = UpgradeEventType.NovaSnipeMasterDamageUpgrade,
                                    Value = (int) trackerEvent.Data.dictionary[2].vInt.Value });
                                break;

                            case "GallTalentDarkDescentUpgrade":
                                playerIDDictionary[(int) trackerEvent.Data.dictionary[0].vInt.Value].UpgradeEvents.Add(new UpgradeEvent {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    UpgradeEventType = UpgradeEventType.GallTalentDarkDescentUpgrade,
                                    Value = (int) trackerEvent.Data.dictionary[2].vInt.Value });
                                break;

                            default:
                                // New Upgrade Event - let's log it until we can identify and properly track it
                                playerIDDictionary[(int) trackerEvent.Data.dictionary[0].vInt.Value].MiscellaneousUpgradeEventDictionary[trackerEvent.Data.dictionary[1].blobText] = true;
                                break;
                        }
                        break;

                    case ReplayTrackerEvents.TrackerEventType.StatGameEvent:
                        switch (trackerEvent.Data.dictionary[0].blobText)
                        {
                            case "GameStart": // {StatGameEvent: {"GameStart", , , [{{"MapSizeX"}, 248}, {{"MapSizeY"}, 208}]}}
                                if (trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[0].dictionary[0].blobText == "MapSizeX" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[1].dictionary[0].dictionary[0].blobText == "MapSizeY")
                                    replay.MapSize = new Point {
                                        X = (int)trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value,
                                        Y = (int)trackerEvent.Data.dictionary[3].optionalData.array[1].dictionary[1].vInt.Value };
                                break;

                            case "PlayerInit": // {StatGameEvent: {"PlayerInit", [{{"Controller"}, "User"}, {{"ToonHandle"}, "1-Hero-1-XXXXX"}], [{{"PlayerID"}, 1}, {{"Team"}, 1}], }}
                                if (trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText == "Computer")
                                    return;
                                else if (trackerEvent.Data.dictionary[1].optionalData.array[1].dictionary[0].dictionary[0].blobText == "ToonHandle" &&
                                    trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[0].dictionary[0].blobText == "PlayerID")
                                        playerIDDictionary[(int)trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value] = replay.Players.Single(i => i.BattleNetId == int.Parse(trackerEvent.Data.dictionary[1].optionalData.array[1].dictionary[1].blobText.Split('-').Last()));
                                break;

                            case "LevelUp": // {StatGameEvent: {"LevelUp", , [{{"PlayerID"}, 6}, {{"Level"}, 1}], }}
                                if (trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[0].dictionary[0].blobText == "PlayerID" &&
                                    trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[0].dictionary[0].blobText == "Level")
                                {
                                    var team = playerIDDictionary[(int)trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value].Team;
                                    var level = (int)trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value;

                                    if (!replay.TeamLevels[team].ContainsKey(level))
                                        replay.TeamLevels[team][level] = trackerEvent.TimeSpan;
                                }
                                break;

                            case "EndOfGameTalentChoices": // {StatGameEvent: {"EndOfGameTalentChoices", [{{"Hero"}, "HeroAbathur"}, {{"Win/Loss"}, "Win"}, {{"Map"}, "HauntedWoods"}, {{"Tier 1 Choice"}, "AbathurMasteryRegenerativeMicrobes"}, {{"Tier 2 Choice"}, "AbathurSymbioteCarapaceSustainedCarapace"}, {{"Tier 3 Choice"}, "AbathurMasteryNeedlespine"}, {{"Tier 4 Choice"}, "AbathurHeroicAbilityUltimateEvolution"}, {{"Tier 5 Choice"}, "AbathurSymbioteSpikeBurstSomaTransference"}, {{"Tier 6 Choice"}, "AbathurVolatileMutation"}, {{"Tier 7 Choice"}, "AbathurMasteryLocustMaster"}], [{{"PlayerID"}, 1}, {{"Level"}, 24}], }}
                                break;

                            case "TalentChosen": // {StatGameEvent: {"TalentChosen", [{{"PurchaseName"}, "NovaCombatStyleAdvancedCloaking"}], [{{"PlayerID"}, 6}], }}
                                if (trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[0].dictionary[0].blobText == "PlayerID" &&
                                    trackerEvent.Data.dictionary[1].optionalData != null &&
                                    trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[0].dictionary[0].blobText == "PurchaseName")
                                {
                                    var playerID = (int)trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value;

                                    if (!playerIDTalentIndexDictionary.ContainsKey(playerID))
                                        playerIDTalentIndexDictionary[playerID] = 0;

                                    if (playerIDDictionary[playerID].Talents.Length > playerIDTalentIndexDictionary[playerID])
                                        playerIDDictionary[playerID].Talents[playerIDTalentIndexDictionary[playerID]++].TalentName = trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText;
                                    else
                                        // A talent was selected while a player was disconnected
                                        // This makes it more difficult to match a 'TalentName' with a 'TalentID'
                                        // Since this is rare, I'll just clear all 'TalentName' for that player
                                        foreach (var talent in playerIDDictionary[playerID].Talents)
                                            talent.TalentName = null;
                                }
                                break;

                            case "PeriodicXPBreakdown": // {StatGameEvent: {"PeriodicXPBreakdown", , [{{"Team"}, 1}, {{"TeamLevel"}, 9}], [{{"GameTime"}, 420}, {{"PreviousGameTime"}, 360}, {{"MinionXP"}, 10877}, {{"CreepXP"}, 0}, {{"StructureXP"}, 1200}, {{"HeroXP"}, 3202}, {{"TrickleXP"}, 7700}]}}
                                if (trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[0].dictionary[0].blobText == "TeamLevel" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[0].dictionary[0].blobText == "GameTime" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[1].dictionary[0].dictionary[0].blobText == "PreviousGameTime" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[2].dictionary[0].dictionary[0].blobText == "MinionXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[3].dictionary[0].dictionary[0].blobText == "CreepXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[4].dictionary[0].dictionary[0].blobText == "StructureXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[5].dictionary[0].dictionary[0].blobText == "HeroXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[6].dictionary[0].dictionary[0].blobText == "TrickleXP")
                                        replay.TeamPeriodicXPBreakdown[(int)trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value - 1].Add(new PeriodicXPBreakdown {
                                            TeamLevel = (int)trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value,
                                            TimeSpan = trackerEvent.TimeSpan,
                                            MinionXP = (int)trackerEvent.Data.dictionary[3].optionalData.array[2].dictionary[1].vInt.Value,
                                            CreepXP = (int)trackerEvent.Data.dictionary[3].optionalData.array[3].dictionary[1].vInt.Value,
                                            StructureXP = (int)trackerEvent.Data.dictionary[3].optionalData.array[4].dictionary[1].vInt.Value,
                                            HeroXP = (int)trackerEvent.Data.dictionary[3].optionalData.array[5].dictionary[1].vInt.Value,
                                            TrickleXP = (int)trackerEvent.Data.dictionary[3].optionalData.array[6].dictionary[1].vInt.Value });
                                break;

                            case "EndOfGameXPBreakdown": // {StatGameEvent: {"EndOfGameXPBreakdown", , [{{"PlayerID"}, 4}], [{{"MinionXP"}, 31222}, {{"CreepXP"}, 1476}, {{"StructureXP"}, 10550}, {{"HeroXP"}, 22676}, {{"TrickleXP"}, 27280}]}}
                                if (trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[0].dictionary[0].blobText == "PlayerID" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[0].dictionary[0].blobText == "MinionXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[1].dictionary[0].dictionary[0].blobText == "CreepXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[2].dictionary[0].dictionary[0].blobText == "StructureXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[3].dictionary[0].dictionary[0].blobText == "HeroXP" &&
                                    trackerEvent.Data.dictionary[3].optionalData.array[4].dictionary[0].dictionary[0].blobText == "TrickleXP" &&
                                    (!replay.TeamPeriodicXPBreakdown[playerIDDictionary[(int) trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value].Team].Any() || replay.TeamPeriodicXPBreakdown[playerIDDictionary[(int) trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value].Team].Last().TimeSpan != trackerEvent.TimeSpan))
                                        replay.TeamPeriodicXPBreakdown[playerIDDictionary[(int) trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value].Team].Add(new PeriodicXPBreakdown {
                                            TeamLevel = replay.TeamLevels[playerIDDictionary[(int) trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value].Team].Keys.Max(),
                                            TimeSpan = trackerEvent.TimeSpan,
                                            MinionXP = (int) trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value,
                                            CreepXP = (int) trackerEvent.Data.dictionary[3].optionalData.array[1].dictionary[1].vInt.Value,
                                            StructureXP = (int) trackerEvent.Data.dictionary[3].optionalData.array[2].dictionary[1].vInt.Value,
                                            HeroXP = (int) trackerEvent.Data.dictionary[3].optionalData.array[3].dictionary[1].vInt.Value,
                                            TrickleXP = (int) trackerEvent.Data.dictionary[3].optionalData.array[4].dictionary[1].vInt.Value });
                                break;

                            case "TownStructureInit": break;        // {StatGameEvent: {"TownStructureInit", , [{{"TownID"}, 5}, {{"Team"}, 1}, {{"Lane"}, 3}], [{{"PositionX"}, 59}, {{"PositionY"}, 93}]}}
                            case "JungleCampInit": break;           // {StatGameEvent: {"JungleCampInit", , [{{"CampID"}, 1}], [{{"PositionX"}, 101}, {{"PositionY"}, 74}]}}
                            case "PlayerSpawned": break;            // {StatGameEvent: {"PlayerSpawned", [{{"Hero"}, "HeroLeoric"}], [{{"PlayerID"}, 1}], }}
                            case "GatesOpen": break;                // {StatGameEvent: {"GatesOpen", , , }}
                            case "PlayerDeath": break;              // {StatGameEvent: {"PlayerDeath", , [{{"PlayerID"}, 8}, {{"KillingPlayer"}, 1}, {{"KillingPlayer"}, 2}, {{"KillingPlayer"}, 3}, {{"KillingPlayer"}, 4}, {{"KillingPlayer"}, 5}], [{{"PositionX"}, 130}, {{"PositionY"}, 80}]}}
                            case "RegenGlobePickedUp": break;       // {StatGameEvent: {"RegenGlobePickedUp", , [{{"PlayerID"}, 1}], }}

                            case "EndOfGameRegenMasterStacks":      // {StatGameEvent: {"EndOfGameRegenMasterStacks", [{{"Hero"}, "HeroZeratul"}], [{{"PlayerID"}, 7}, {{"Stack Count"}, 23}], }}
                                playerIDDictionary[(int) trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value].UpgradeEvents.Add(new UpgradeEvent {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    UpgradeEventType = UpgradeEventType.RegenMasterStacks,
                                    Value = (int) trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value });
                                break;

                            case "EndOfGameMarksmanStacks":         // {StatGameEvent: {"EndOfGameMarksmanStacks", [{{"Hero"}, "HeroFalstad"}], [{{"PlayerID"}, 4}, {{"Stack Count"}, 400}], }}
                                playerIDDictionary[(int) trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value].UpgradeEvents.Add(new UpgradeEvent {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    UpgradeEventType = UpgradeEventType.MarksmanStacks,
                                    Value = (int) trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value });
                                break;

                            case "JungleCampCapture":               // {StatGameEvent: {"JungleCampCapture", [{{"CampType"}, "Boss Camp"}], [{{"CampID"}, 1}], [{{"TeamID"}, 1}]}}
                                if (trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText == "Boss Camp")
                                {
                                    var teamID = trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;
                                    Player playerKilledBy = null;

                                    var bossUnitsKilled = replay.Units.Where(i =>
                                        (i.Name == "JungleGraveGolemDefender" || i.Name == "MercHorsemanDefender") &&
                                        i.PlayerKilledBy != null &&
                                        i.PlayerKilledBy.Team == teamID &&
                                        i.TimeSpanDied > trackerEvent.TimeSpan.Add(TimeSpan.FromSeconds(-30)) &&
                                        i.TimeSpanDied < trackerEvent.TimeSpan.Add(TimeSpan.FromSeconds(30))).ToArray();

                                    if (bossUnitsKilled.Length == 1)
                                        playerKilledBy = bossUnitsKilled.Single().PlayerKilledBy;

                                    replay.TeamObjectives[teamID].Add(new TeamObjective {
                                        Player = playerKilledBy,
                                        TimeSpan = trackerEvent.TimeSpan,
                                        TeamObjectiveType = TeamObjectiveType.BossCampCaptureWithCampID,
                                        Value = (int) trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value });
                                }
                                    
                                break;

                            case "TownStructureDeath": break;       // {StatGameEvent: {"TownStructureDeath", , [{{"TownID"}, 8}, {{"KillingPlayer"}, 1}, {{"KillingPlayer"}, 2}, {{"KillingPlayer"}, 3}, {{"KillingPlayer"}, 4}, {{"KillingPlayer"}, 5}], }}
                            case "EndOfGameTimeSpentDead": break;   // {StatGameEvent: {"EndOfGameTimeSpentDead", , [{{"PlayerID"}, 2}], [{{"Time"}, 162}]}}

                            // Map Objectives

                                // Towers of Doom
                            case "Altar Captured":                  // {StatGameEvent: {"Altar Captured", , [{{"Firing Team"}, 2}, {{"Towns Owned"}, 3}], }}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.TowersOfDoomAltarCapturedWithTeamTownsOwned,
                                    Value = (int) trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value });
                                break;
                            case "Town Captured": break;            // {StatGameEvent: {"Town Captured", , [{{"New Owner"}, 12}], }}
                            case "Six Town Event Start":            // {StatGameEvent: {"Six Town Event Start", , [{{"Owning Team"}, 1}], [{{"Start Time"}, 742}]}}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.TowersOfDoomSixTownEventStartWithEventDurationSeconds,
                                    Value = (int) (replay.ReplayLength - trackerEvent.TimeSpan).TotalSeconds - 10 });
                                break;
                            case "Six Town Event End":              // {StatGameEvent: {"Six Town Event End", , [{{"Owning Team"}, 1}], [{{"End Time"}, 747}]}}
                                var mostRecentSixTownEventStartTeamObjective = replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value - 1].Where(i => i.TeamObjectiveType == TeamObjectiveType.TowersOfDoomSixTownEventStartWithEventDurationSeconds).OrderByDescending(i => i.TimeSpan).First();
                                mostRecentSixTownEventStartTeamObjective.Value = (int) (trackerEvent.TimeSpan - mostRecentSixTownEventStartTeamObjective.TimeSpan).TotalSeconds;
                                break;

                                // Sky Temple
                            case "SkyTempleActivated": break;       // {StatGameEvent: {"SkyTempleActivated", , [{{"Event"}, 1}, {{"TempleID"}, 1}], }}
                            case "SkyTempleCaptured": break;        // {StatGameEvent: {"SkyTempleCaptured", , [{{"Event"}, 1}, {{"TempleID"}, 2}, {{"TeamID"}, 2}], }}
                            case "SkyTempleShotsFired":             // {StatGameEvent: {"SkyTempleShotsFired", , [{{"Event"}, 1}, {{"TempleID"}, 2}, {{"TeamID"}, 2}], [{{"SkyTempleShotsDamage"}, 450}]}}
                                if (trackerEvent.Data.dictionary[2].optionalData.array[2].dictionary[1].vInt.Value == 0) // Not sure why, but sometimes 'TeamID' = 0.  I've seen it 3 times in about ~60 Sky Temple games
                                    break;

                                var recentSkyTempleShotsFiredTeamObjective = replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[2].dictionary[1].vInt.Value - 1].Where(i => i.TeamObjectiveType == TeamObjectiveType.SkyTempleShotsFiredWithSkyTempleShotsDamage && i.TimeSpan > trackerEvent.TimeSpan.Add(TimeSpan.FromSeconds(-130))).SingleOrDefault();

                                if (recentSkyTempleShotsFiredTeamObjective != null)
                                    recentSkyTempleShotsFiredTeamObjective.Value += (int) trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value;
                                else
                                    replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[2].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                        TimeSpan = trackerEvent.TimeSpan,
                                        TeamObjectiveType = TeamObjectiveType.SkyTempleShotsFiredWithSkyTempleShotsDamage,
                                        Value = (int) trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value });
                                break;

                                // Battlefield of Eternity
                            case "Immortal Defeated":               // {StatGameEvent: {"Immortal Defeated", , [{{"Event"}, 1}, {{"Winning Team"}, 1}, {{"Immortal Fight Duration"}, 62}], [{{"Immortal Power Percent"}, 14}]}}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.BattlefieldOfEternityImmortalFightEndWithPowerPercent,
                                    Value = (int) trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value });
                                break;
                            case "Boss Duel Started": break;        // {StatGameEvent: {"Boss Duel Started", , [{{"Boss Duel Number"}, 1}], }}

                                // Tomb of the Spider Queen
                            case "SoulEatersSpawned":               // {StatGameEvent: {"SoulEatersSpawned", , [{{"Event"}, 1}, {{"TeamScore"}, 50}, {{"OpponentScore"}, 5}], [{{"TeamID"}, 2}]}}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.TombOfTheSpiderQueenSoulEatersSpawnedWithTeamScore,
                                    Value = (int) trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value });
                                break;

                                // Cursed Hollow
                            case "TributeCollected":                // {StatGameEvent: {"TributeCollected", , [{{"Event"}, 1}], [{{"TeamID"}, 2}]}}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.CursedHollowTributeCollectedWithTotalTeamTributes,
                                    Value = replay.TeamObjectives[trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1].Count(i => i.TeamObjectiveType == TeamObjectiveType.CursedHollowTributeCollectedWithTotalTeamTributes) + 1 });
                                break;
                            case "RavenCurseActivated": break;      // {StatGameEvent: {"RavenCurseActivated", , [{{"Event"}, 1}, {{"TeamScore"}, 3}, {{"OpponentScore"}, 2}], [{{"TeamID"}, 2}]}}

                                // Blackheart's Bay
                            case "GhostShipCaptured":               // {StatGameEvent: {"GhostShipCaptured", , [{{"Event"}, 1}, {{"TeamScore"}, 10}, {{"OpponentScore"}, 6}], [{{"TeamID"}, 2}]}}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.BlackheartsBayGhostShipCapturedWithCoinCost,
                                    Value = (int) trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value });
                                break;

                                // Garden of Terror - This is populated using Unit data at the top of this function
                            case "GardenTerrorActivated": break;    // {StatGameEvent: {"GardenTerrorActivated", , , [{{"Event"}, 1}, {{"TeamID"}, 2}]}}

                                // Infernal Shrines
                            case "Infernal Shrine Captured":        // {StatGameEvent: {"Infernal Shrine Captured", , [{{"Event"}, 1}, {{"Winning Team"}, 2}, {{"Winning Score"}, 40}, {{"Losing Score"}, 33}], }}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.InfernalShrinesInfernalShrineCapturedWithLosingScore,
                                    Value = (int) trackerEvent.Data.dictionary[2].optionalData.array[3].dictionary[1].vInt.Value });
                                break;
                            case "Punisher Killed":                 // {StatGameEvent: {"Punisher Killed", [{{"Punisher Type"}, "BombardShrine"}], [{{"Event"}, 1}, {{"Owning Team of Punisher"}, 2}, {{"Duration"}, 20}], [{{"Siege Damage Done"}, 726}, {{"Hero Damage Done"}, 0}]}}
                                replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.InfernalShrinesPunisherKilledWithPunisherType,
                                    Value = trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText == "BombardShrine" ? (int) TeamObjectiveInfernalShrinesPunisherType.BombardShrine :
                                            trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText == "ArcaneShrine"  ? (int) TeamObjectiveInfernalShrinesPunisherType.ArcaneShrine :
                                                                                                                                              (int) TeamObjectiveInfernalShrinesPunisherType.FrozenShrine });

                                replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.InfernalShrinesPunisherKilledWithSiegeDamageDone,
                                    Value = (int) trackerEvent.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value });

                                replay.TeamObjectives[trackerEvent.Data.dictionary[2].optionalData.array[1].dictionary[1].vInt.Value - 1].Add(new TeamObjective {
                                    TimeSpan = trackerEvent.TimeSpan,
                                    TeamObjectiveType = TeamObjectiveType.InfernalShrinesPunisherKilledWithHeroDamageDone,
                                    Value = (int) trackerEvent.Data.dictionary[3].optionalData.array[1].dictionary[1].vInt.Value });
                                break;

                            // Dragon Shire - This is populated using Unit data at the top of this function
                            case "DragonKnightActivated": break;    // {StatGameEvent: {"DragonKnightActivated", , [{{"Event"}, 1}], [{{"TeamID"}, 2}]}}

                            default:
                                // New Stat Game Event - let's log it until we can identify and properly track it
                                playerIDDictionary[(int) trackerEvent.Data.dictionary[0].vInt.Value].MiscellaneousUpgradeEventDictionary[trackerEvent.Data.dictionary[0].blobText] = true;
                                break;
                        }
                        break;

                    case ReplayTrackerEvents.TrackerEventType.ScoreResultEvent:
                        var scoreResultEventDictionary = trackerEvent.Data.dictionary[0].array.ToDictionary(i => i.dictionary[0].blobText, i => i.dictionary[1].array.Select(j => j.array.Length == 1 ? (int) j.array[0].dictionary[0].vInt.Value : (int?)null).ToArray());

                        foreach (var scoreResultEventKey in scoreResultEventDictionary.Keys)
                        {
                            var scoreResultEventValueArray = scoreResultEventDictionary[scoreResultEventKey];

                            switch (scoreResultEventKey)
                            {
                                case "Takedowns":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.Takedowns = scoreResultEventValueArray[i].Value;
                                    break;
                                case "SoloKill":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.SoloKills = scoreResultEventValueArray[i].Value;
                                    break;
                                case "Assists":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.Assists = scoreResultEventValueArray[i].Value;
                                    break;
                                case "Deaths":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.Deaths = scoreResultEventValueArray[i].Value;
                                    break;
                                case "HeroDamage":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.HeroDamage = scoreResultEventValueArray[i].Value;
                                    break;
                                case "SiegeDamage":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.SiegeDamage = scoreResultEventValueArray[i].Value;
                                    break;
                                case "StructureDamage":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.StructureDamage = scoreResultEventValueArray[i].Value;
                                    break;
                                case "MinionDamage":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.MinionDamage = scoreResultEventValueArray[i].Value;
                                    break;
                                case "CreepDamage":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.CreepDamage = scoreResultEventValueArray[i].Value;
                                    break;
                                case "SummonDamage":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.SummonDamage = scoreResultEventValueArray[i].Value;
                                    break;
                                case "TimeCCdEnemyHeroes":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue && scoreResultEventValueArray[i].Value > 0)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.TimeCCdEnemyHeroes = TimeSpan.FromSeconds(scoreResultEventValueArray[i].Value);
                                    break;
                                case "Healing":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue && scoreResultEventValueArray[i].Value > 0)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.Healing = scoreResultEventValueArray[i].Value;
                                    break;
                                case "SelfHealing":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.SelfHealing = scoreResultEventValueArray[i].Value;
                                    break;
                                case "DamageTaken":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue && scoreResultEventValueArray[i].Value > 0)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.DamageTaken = scoreResultEventValueArray[i].Value;
                                    break;
                                case "ExperienceContribution":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.ExperienceContribution = scoreResultEventValueArray[i].Value;
                                    break;
                                case "TownKills":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.TownKills = scoreResultEventValueArray[i].Value;
                                    break;
                                case "TimeSpentDead":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.TimeSpentDead = TimeSpan.FromSeconds(scoreResultEventValueArray[i].Value);
                                    break;
                                case "MercCampCaptures":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.MercCampCaptures = scoreResultEventValueArray[i].Value;
                                    break;
                                case "WatchTowerCaptures":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.WatchTowerCaptures = scoreResultEventValueArray[i].Value;
                                    break;
                                case "MetaExperience":
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].ScoreResult.MetaExperience = scoreResultEventValueArray[i].Value;
                                    break;

                                default:
                                    for (var i = 0; i < scoreResultEventValueArray.Length; i++)
                                        if (scoreResultEventValueArray[i].HasValue)
                                            replay.ClientListByWorkingSetSlotID[i].MiscellaneousScoreResultEventDictionary[scoreResultEventKey] = scoreResultEventValueArray[i].Value;
                                    break;
                            }
                        }
                        break;
                }

            for (var i = 0; i < replay.TeamObjectives.Length; i++)
                replay.TeamObjectives[i] = replay.TeamObjectives[i].OrderBy(j => j.TimeSpan).ToList();
        }
    }
}
