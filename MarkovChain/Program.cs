using System;

using System.Collections;
using System.Collections.Generic;

namespace MarkovChain {
	class Program {
		static void Main() {
			Ingesting.IngestOptions opts = new Ingesting.IngestOptions {
				infile_csv = "m.csv",
				csv_column = "Content",
				regex_filters = new Tuple<string, string>[]{
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
				gram_size = 2,
				outfile_markov = "test.markov"
			};
#if false
			Ingesting.Pipeline pipe = new Ingesting.Pipeline(opts);
			if (!pipe.Run()) {
				Console.WriteLine("Some sort of error occured.");
			}

			Structs.MarkovStructure resultant = pipe.finished_markovstruct;
			resultant.WriteFile(opts.outfile_markov);


			Random r = new Random();
			do {
				for (int j = 0; j < 1000; j++) {
					Console.Out.WriteLine(resultant.GenerateSentence(r));
				}
				Console.Out.Flush();
			} while (Console.ReadKey().Key != ConsoleKey.Q);
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
			 *		H: based on A, rearrange dictionary and update structures to point to regular thing
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
