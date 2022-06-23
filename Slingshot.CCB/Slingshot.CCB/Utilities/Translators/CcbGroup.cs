﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Slingshot.Core;
using Slingshot.Core.Model;

namespace Slingshot.CCB.Utilities.Translators
{
    public static class CcbGroup
    {
        public static List<Group> Translate( XElement inputGroup, bool exportDirectorsAsGroups = true )
        {
            List<Group> groups = new List<Group>();

            int? departmentId = null;
            int? directorId = null;

            var group = new Group();

            group.Id = inputGroup.Attribute( "id" ).Value.AsInteger();
            group.Name = inputGroup.Element( "name" )?.Value;
            group.Description = inputGroup.Element( "description" )?.Value.RemoveCrLf();
            group.GroupTypeId = inputGroup.Element( "group_type" ).Attribute( "id" ).Value.AsInteger();
            group.CampusId = inputGroup.Element( "campus" ).Attribute( "id" ).Value.AsIntegerOrNull();
            group.Capacity = inputGroup.Element( "group_capacity" ).Value.AsIntegerOrNull();
            group.IsActive = !inputGroup.Element( "inactive" ).Value.AsBoolean();
            group.IsPublic = inputGroup.Element( "public_search_listed" ).Value.AsBoolean();
            group.MeetingDay = inputGroup.Element( "meeting_day" ).Value;
            group.MeetingTime = inputGroup.Element( "meeting_time" ).Value;

            ProcessBooleanAttribute( group, inputGroup.Element( "childcare_provided" ), "HasChildcare" );

            if ( group.GroupTypeId == 0 )
            {
                group.GroupTypeId = CcbApi.GROUPTYPE_UNKNOWN_ID;
            }

            groups.Add( group );

            var hasDepartment = inputGroup.Element( "department" ) != null && inputGroup.Element( "department" ).Attribute( "id" ) != null && inputGroup.Element( "department" ).Attribute( "id" ).Value.IsNotNullOrWhitespace();
            var hasDirector = inputGroup.Element( "director" ) != null && inputGroup.Element( "director" ).Attribute( "id" ) != null && inputGroup.Element( "director" ).Attribute( "id" ).Value.IsNotNullOrWhitespace();
            
            MD5 md5Hasher = MD5.Create();
            
            // add the department as a group with id set to hash of 9999 + department id to create a unique group id for it
            if ( hasDepartment )
            {
                var hashedDepartmentId = md5Hasher.ComputeHash( Encoding.UTF8.GetBytes( $@"
                     {9999}
                     {inputGroup.Element( "department" ).Attribute( "id" ).Value}
                    " ) );
                departmentId = Math.Abs( BitConverter.ToInt32( hashedDepartmentId, 0 ) ); // used abs to ensure positive number

                var departmentName = inputGroup.Element( "department" ).Value;
                if ( departmentName.IsNullOrWhiteSpace() )
                {
                    departmentName = "No Department Name";
                }
                var departmentGroup = new Group();
                departmentGroup.Id = departmentId.Value;
                departmentGroup.IsActive = true;
                departmentGroup.Name = departmentName;
                departmentGroup.GroupTypeId = 9999;

                if ( !exportDirectorsAsGroups && hasDirector )
                {
                    departmentGroup.GroupMembers.Add( new GroupMember { PersonId = inputGroup.Element( "director" ).Attribute( "id" ).Value.AsInteger(), Role = "Director", GroupId = departmentGroup.Id } );
                }
                groups.Add( departmentGroup );
            }

            if ( exportDirectorsAsGroups && hasDirector )
            {
                // add the director as a group with id set to hash of 9998 + its id + its department id (if any) to create a unique group id for it
                var departmentIdString = departmentId.HasValue ? departmentId.Value.ToString() : string.Empty;
                var hashedDirectorId = md5Hasher.ComputeHash( Encoding.UTF8.GetBytes( $@"
                     {9998}
                     {inputGroup.Element( "director" ).Attribute( "id" ).Value}
                     {departmentIdString}
                    " ) );
                directorId = Math.Abs( BitConverter.ToInt32( hashedDirectorId, 0 ) ); // used abs to ensure positive number

                var directorGroup = new Group();
                directorGroup.Id = directorId.Value;
                directorGroup.IsActive = true;
                directorGroup.Name = inputGroup.Element( "director" ).Element( "full_name" ).Value;
                directorGroup.GroupTypeId = 9998;

                // add parent group of the department if it exists
                if ( departmentId.HasValue )
                {
                    directorGroup.ParentGroupId = departmentId.Value;
                }

                directorGroup.GroupMembers.Add( new GroupMember { PersonId = inputGroup.Element( "director" ).Attribute( "id" ).Value.AsInteger(), Role = "Leader", GroupId = directorGroup.Id } );

                groups.Add( directorGroup );
            }

            // add leader
            if ( inputGroup.Element( "main_leader" ).Attribute( "id" ) != null && inputGroup.Element( "main_leader" ).Attribute( "id" ).Value.AsInteger() != 0 )
            {
                group.GroupMembers.Add( new GroupMember { PersonId = inputGroup.Element( "main_leader" ).Attribute( "id" ).Value.AsInteger(), Role = "Leader", GroupId = group.Id } );
            }

            // add assistant leaders
            if ( inputGroup.Element( "leaders" ) != null )
            {
                foreach ( var leaderNode in inputGroup.Element( "leaders" ).Elements( "leader" ) )
                {
                    group.GroupMembers.Add( new GroupMember { PersonId = leaderNode.Attribute( "id" ).Value.AsInteger(), Role = "Assistant Leader", GroupId = group.Id } );
                }
            }

            // add participants
            if ( inputGroup.Element( "participants" ) != null )
            {
                foreach ( var participantNode in inputGroup.Element( "participants" ).Elements( "participant" ) )
                {
                    group.GroupMembers.Add( new GroupMember { PersonId = participantNode.Attribute( "id" ).Value.AsInteger(), Role = "Member", GroupId = group.Id } );
                }
            }

            var addressList = inputGroup.Element( "addresses" ).Elements( "address" );
            foreach ( var address in addressList )
            {
                if ( address.Element( "street_address" ) != null && address.Element( "street_address" ).Value.IsNotNullOrWhitespace() )
                {
                    var importAddress = new GroupAddress();
                    importAddress.GroupId = group.Id;
                    importAddress.Street1 = address.Element( "street_address" ).Value.RemoveCrLf();
                    importAddress.City = address.Element( "city" ).Value;
                    importAddress.State = address.Element( "state" ).Value;
                    importAddress.PostalCode = address.Element( "zip" ).Value;
                    importAddress.Latitude = address.Element( "latitude" )?.Value;
                    importAddress.Longitude = address.Element( "longitude" )?.Value;
                    importAddress.Country = address.Element( "country" )?.Value;

                    var addressType = address.Attribute( "type" ).Value;

                    switch ( addressType )
                    {
                        case "mailing":
                        case "home":
                            {
                                importAddress.AddressType = AddressType.Home;
                                importAddress.IsMailing = addressType.Equals( "mailing" );
                                break;
                            }
                        case "work":
                            {
                                importAddress.AddressType = AddressType.Work;
                                break;
                            }
                        case "other":
                            {
                                importAddress.AddressType = AddressType.Other;
                                break;
                            }
                    }

                    // only add the address if we have a valid address
                    if ( importAddress.Street1.IsNotNullOrWhitespace() && importAddress.City.IsNotNullOrWhitespace() && importAddress.PostalCode.IsNotNullOrWhitespace() )
                    {
                        group.Addresses.Add( importAddress );
                    }
                }
            }

            // determine the parent group
            if ( exportDirectorsAsGroups && directorId.HasValue )
            {
                group.ParentGroupId = directorId.Value;
            }
            else if ( departmentId.HasValue )
            {
                group.ParentGroupId = departmentId.Value;
            }

            return groups;
        }

        /// <summary>
        /// Processes the string attribute.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="element">The element.</param>
        /// <param name="attributeKey">The attribute key.</param>
        private static void ProcessStringAttribute( Group group, XElement element, string attributeKey )
        {
            if ( element != null && element.Value.IsNotNullOrWhitespace() )
            {
                group.Attributes.Add( new GroupAttributeValue { GroupId = group.Id, AttributeKey = attributeKey, AttributeValue = element.Value } );
            }
        }

        /// <summary>
        /// Processes the boolean attribute.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="element">The element.</param>
        /// <param name="attributeKey">The attribute key.</param>
        private static void ProcessBooleanAttribute( Group group, XElement element, string attributeKey )
        {
            if ( element != null && element.Value.IsNotNullOrWhitespace() )
            {
                group.Attributes.Add( new GroupAttributeValue { GroupId = group.Id, AttributeKey = attributeKey, AttributeValue = element.Value.AsBoolean().ToString() } );
            }
        }

        /// <summary>
        /// Processes the datetime attribute.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="element">The element.</param>
        /// <param name="attributeKey">The attribute key.</param>
        private static void ProcessDatetimeAttribute( Group group, XElement element, string attributeKey )
        {
            if ( element != null && element.Value.AsDateTime().HasValue )
            {
                group.Attributes.Add( new GroupAttributeValue { GroupId = group.Id, AttributeKey = attributeKey, AttributeValue = element.Value.AsDateTime().ToString() } );
            }
        }
    }
}