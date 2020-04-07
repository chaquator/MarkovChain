using System;
using System.Collections.Generic;

using System.Linq;

namespace MarkovChain {
	class Program {
		static void Main() {
			// TODO: seperate between ingesting and input/output options
			Ingesting.IngestOptions opts = new Ingesting.IngestOptions {
				infileCSV = "m.csv",
				csvColumn = "Content",
				regexFilters = new Tuple<string, string>[]{
					Tuple.Create(@"\b?@\S+\s*", " "), // filter @person's
					Tuple.Create(@"\b?https?.*\s*", " "), // filter URLs
					Tuple.Create(@"`+[^`]*`+", " "), // Filter code blocks
					Tuple.Create(@"[\*]+", " "), // Filter italics and bold and all
					Tuple.Create(@"\b?\W+\s*", " "), // filter whole non-words (e.g. "->")

					// THESE ALWAYS GO LAST
					Tuple.Create(@"^\s+",""), // opening spaces
					Tuple.Create(@"\s+$", ""), // closing spaces
					Tuple.Create(@"\s{2,}", " ") // excess space (also handles newlines)
				},
				gramSize = 2,
				outfileMarkov = "test.markov"
			};
#if true
#else
			Structs.MarkovStructure A = Structs.MarkovStructure.ReadFile("A.markov");
			Structs.MarkovStructure H = Structs.MarkovStructure.ReadFile("H.markov");
			var C = A.Combine(H);
			Console.WriteLine(H.ToString());
#endif

			/* Unit test planning for MarkovStructure combine functions
			 *	Unit tests for MarkovStructure Combine
			 *		Test combining the same structure (small test, real test)
			 *		Test combining completely different structures (small test, not really sure if it's possible to have a real test)
			 *		Test combining structures with SOME overlap (small, large)
			 *	Unit test for MarkovSegment Combine
			 *		
			 *	Tests:
			 *		A: small same structure -- OK
			 *		B: big same structure
			 *		C: small different
			 *		~~D: big different?~~
			 *		E: small overlap
			 *		F: big overlap
			 *		G: any with empty (if possible)
			 *		H: based on A, rearrange dictionary and update structures to point to regular thing -- OK
			 *		I: final test, when ingesting pipeline is reworked to handle multiple users, combine them all together`
			 */

			/*	TODO: Plan out fully fledged options and implement them
			 *		-	Maybe ingest unique regex filters, can be some sort of input csv file idk
			 *		-	Maybe split runtime functionality into ingest and create from file
			 *		-		Maybe ingest has a flag to create from ingested
			 *		-	For creating, read from stdin or from passed "-input" parameter to produce in some sed shell like quality
			 */
		}
	}
}
