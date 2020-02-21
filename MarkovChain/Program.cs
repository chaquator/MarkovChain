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
#if true
			Ingesting.Pipeline pipe = new Ingesting.Pipeline(opts);
			if (!pipe.Run()) {
				Console.WriteLine("Some sort of error occured.");
			}

			Structs.MarkovStructure resultant = pipe.finished_markovstruct;
			//resultant.WriteFile(opts.outfile_markov);

			Random r = new Random();
			do {
				for (int j = 0; j < 1000; j++) {
					Console.WriteLine(resultant.GenerateSentence(r));
				}
			} while (Console.ReadKey().Key != ConsoleKey.Q);
#endif

			/*	TODO: implement proper options
			 *		-	Maybe ingest unique regex filters, can be some sort of csv
			 *		-	Maybe split runtime functionality into ingest and create from file
			 *		-		Maybe ingest has a flag to create from ingested
			 *		-	For creating, read from stdin or from passed "-input" parameter to produce in some sed shell like quality
			 */
		}
	}
}
