using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The join behind al_GetRoleHolders. Every interesting case is a DISAGREEMENT between
    /// the mapping table and the web role association, so the merge has to keep a person
    /// who appears in only one of them.
    /// </summary>
    public class RoleHoldersTests
    {
        private static readonly Guid MappingId = Guid.Parse("55555555-eeee-4eee-8eee-555555555555");

        private static Entity Mapping(string email, bool active, Guid? id = null)
        {
            var row = new Entity("al_userrolemapping", id ?? MappingId);
            row["al_useremail"] = email;
            row["statecode"] = new OptionSetValue(active ? 0 : 1);
            return row;
        }

        private static Entity Contact(string email, string name)
        {
            var row = new Entity("contact", Guid.NewGuid());
            row["emailaddress1"] = email;
            row["fullname"] = name;
            return row;
        }

        [Fact]
        public void ReportsAConsistentHolderOnce()
        {
            var holders = RoleHolders.Merge(
                new[] { Mapping("a@ascotlloyd.co.uk", true) },
                new[] { Contact("a@ascotlloyd.co.uk", "A Person") });

            var only = Assert.Single(holders);
            Assert.Equal("a@ascotlloyd.co.uk", only.Email);
            Assert.Equal("A Person", only.Name);
            Assert.Equal(MappingId, only.MappingId);
            Assert.True(only.MappingActive);
            Assert.True(only.Associated);
        }

        [Fact]
        public void KeepsAPortalOnlyGrantThatHasNoMappingAtAll()
        {
            var holders = RoleHolders.Merge(
                new Entity[0],
                new[] { Contact("portal@ascotlloyd.co.uk", "Portal Person") });

            var only = Assert.Single(holders);
            Assert.Null(only.MappingId);
            Assert.Null(only.MappingActive);
            Assert.True(only.Associated);
        }

        [Fact]
        public void KeepsAWithdrawnMappingWhoseAssociationSurvived()
        {
            // The silent un-withdraw: the app says withdrawn, the portal still grants it.
            var holders = RoleHolders.Merge(
                new[] { Mapping("z@ascotlloyd.co.uk", false) },
                new[] { Contact("z@ascotlloyd.co.uk", "Z Person") });

            var only = Assert.Single(holders);
            Assert.False(only.MappingActive);
            Assert.True(only.Associated);
        }

        [Fact]
        public void KeepsAnActiveMappingWhoseAssociationWasRemoved()
        {
            var holders = RoleHolders.Merge(
                new[] { Mapping("gone@ascotlloyd.co.uk", true) },
                new Entity[0]);

            var only = Assert.Single(holders);
            Assert.True(only.MappingActive);
            Assert.False(only.Associated);
        }

        [Fact]
        public void MatchesTheTwoSourcesRegardlessOfEmailCasing()
        {
            var holders = RoleHolders.Merge(
                new[] { Mapping("Mixed.Case@AscotLloyd.co.uk", true) },
                new[] { Contact("mixed.case@ascotlloyd.co.uk", "Mixed Case") });

            Assert.Single(holders);
            Assert.True(holders[0].Associated);
        }

        [Fact]
        public void OrdersByEmailSoTheListIsStableBetweenReads()
        {
            var holders = RoleHolders.Merge(
                new[]
                {
                    Mapping("z@ascotlloyd.co.uk", true, Guid.NewGuid()),
                    Mapping("a@ascotlloyd.co.uk", true, Guid.NewGuid()),
                },
                new Entity[0]);

            Assert.Equal(new[] { "a@ascotlloyd.co.uk", "z@ascotlloyd.co.uk" }, holders.Select(h => h.Email));
        }

        [Fact]
        public void EmitsJsonTheClientCanParseIncludingNulls()
        {
            var json = RoleHolders.ToJson(RoleHolders.Merge(
                new Entity[0],
                new[] { Contact("q\"uote@ascotlloyd.co.uk", "Quote \"Person\"") }));

            Assert.Contains("\"mappingId\":null", json);
            Assert.Contains("\"mappingActive\":null", json);
            Assert.Contains("\"associated\":true", json);
            Assert.Contains("\\\"", json);
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        [Fact]
        public void EmitsAnEmptyArrayRatherThanNothingWhenNobodyHoldsTheRole()
        {
            Assert.Equal("[]", RoleHolders.ToJson(new List<RoleHolder>()));
        }
    }
}
