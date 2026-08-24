//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM
{
    public enum ComparisonOperator
    {
        BitAnd,
        EndsWith,
        Equals,
        EqualOrGreaterThan,
        GreaterThan,
        EqualOrLessThan,
        LessThan,
        Includes,
        IsMemberOf,
        MatchesExpression,
        NotBitAnd,
        NotEquals,
        NotMatchesExpression,

        // Appended rather than placed alphabetically: the values are ordinal, and inserting
        // would renumber every member after the insertion point.
        //
        // RFC 7644 section 3.4.2.2 requires both. They were absent, so a filter using "co" or
        // "sw" parsed and then failed with NotSupportedException - and reported the wrong
        // operator while doing it, which is why the gap was not obvious.
        Contains,
        StartsWith
    }
}