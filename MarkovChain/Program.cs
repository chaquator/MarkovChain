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
#endif
			// RANDOM WEIGHTED CHOICE TEST
			Dictionary<char, int> m = new Dictionary<char, int> {
				{ 'a', 5 },
				{ 'b', 2 },
				{ 'c', 2 },
				{ 'd', 2 },
				{ 'e', 1 }
			};
			int[] r = new int[m.Count];
			int t = 0;
			int i = 0;
			foreach (var kvp in m) {
				t += kvp.Value;
				r[i++] = t;
			}

			bool s = true;
			do {
				if (s) {
					s = false;
				} else {
					Console.Write('\n');
				}

				char[] k = new char[m.Count];
				m.Keys.CopyTo(k, 0);
				Console.WriteLine(Utils.RandomWeightedChoice(k, r, (c) => m[c]));

				Console.Write("Continue? (y/n) ");

			} while (Console.ReadKey().Key != ConsoleKey.N);

			Console.Write('\n');

			/*	TODO: implement proper options
			 *		-	Maybe ingest unique regex filters, can be some sort of csv
			 *		-	Maybe split runtime functionality into ingest and create from file
			 *		-		Maybe ingest has a flag to create from ingested
			 *		-	For creating, read from stdin or from passed "-input" parameter to produce in some sed shell like quality
			 */
		}
	}
}
