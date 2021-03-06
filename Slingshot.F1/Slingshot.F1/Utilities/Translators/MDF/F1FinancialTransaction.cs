﻿using System;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Slingshot.Core.Model;
using System.Collections.Generic;

namespace Slingshot.F1.Utilities.Translators.MDB
{
    public static class F1FinancialTransaction
    {
        public static FinancialTransaction Translate( DataRow row, Dictionary<int, HeadOfHousehold> headOfHouseHolds, HashSet<int> companyHouseholdIds )
        {
            var transaction = new FinancialTransaction();
            var individualId = row.Field<int?>( "Individual_ID" );
            var householdId = row.Field<int?>( "household_id" );
            var isCompany = householdId.HasValue && companyHouseholdIds.Contains( householdId.Value );

            if ( individualId.HasValue )
            {
                transaction.AuthorizedPersonId = individualId.Value;
            }
            else if ( isCompany )
            {
                transaction.AuthorizedPersonId = F1Company.GetCompanyAsPersonId( householdId.Value );
            }
            else if ( householdId.HasValue && headOfHouseHolds.TryGetValue( householdId.Value, out var headIndividual ) )
            {
                transaction.AuthorizedPersonId = headIndividual?.IndividualId;
            }

            if ( row.Field<int?>( "BatchID" ).HasValue )
            {
                transaction.BatchId = row.Field<int?>( "BatchID" ).Value;
            }
            else
            {
                transaction.BatchId = 90000000 + int.Parse( row.Field<DateTime?>( "Received_Date" ).Value.ToString( "yyyyMMdd" ) );
            }

            transaction.TransactionDate = row.Field<DateTime?>( "Received_Date" );
            transaction.TransactionCode = row.Field<string>( "Check_Number" );
            transaction.Summary = row.Field<string>( "Memo" );
            transaction.Id = row.Field<int>( "ContributionID" );

            switch ( row.Field<string>( "Contribution_Type_Name" ) )
            {
                case "Cash":
                    transaction.CurrencyType = CurrencyType.Cash;
                    break;
                case "Check":
                    transaction.CurrencyType = CurrencyType.Check;
                    break;
                case "Credit Card":
                    transaction.CurrencyType = CurrencyType.CreditCard;
                    break;
                case "ACH":
                    transaction.CurrencyType = CurrencyType.ACH;
                    break;
                case "Non-Cash":
                    transaction.CurrencyType = CurrencyType.NonCash;
                    break;
                default:
                    transaction.CurrencyType = CurrencyType.Unknown;
                    break;
            }

            var accountId = 0;
            MD5 md5Hasher = MD5.Create();
            byte[] hashed;
            //Set Account Id
            if ( string.IsNullOrWhiteSpace( row.Field<string>( "sub_fund_name" ) ) )
            {
                //Use Hash to create Account ID
                hashed = md5Hasher.ComputeHash( Encoding.UTF8.GetBytes( row.Field<string>( "fund_name" ) ) );

            }
            else
            {
                hashed = md5Hasher.ComputeHash( Encoding.UTF8.GetBytes( row.Field<string>( "fund_name" ) + row.Field<string>( "sub_fund_name" ) ) );
            }

            accountId = Math.Abs( BitConverter.ToInt32( hashed, 0 ) ); // used abs to ensure positive number

            transaction.FinancialTransactionDetails.Add( new FinancialTransactionDetail
            {
                Id = transaction.Id,
                TransactionId = transaction.Id,
                Amount = row.Field<decimal>( "Amount" ),
                AccountId = accountId
            } );

            return transaction;
        }
    }
}
