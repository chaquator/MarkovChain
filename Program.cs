using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;

using System.Text.RegularExpressions; // regex
using Microsoft.VisualBasic.FileIO; // For CSV parsing
using System.IO; // StreamWriter, StreamReader
using System.Text.Json; // JSON

namespace MarkovChain {
	// Here so no function complains for the time being
	public struct SentenceBank {
		public string[] dictionary;
		public int[][] sentences;
	}

	/// <summary>
	/// Class holds several functions for processing data.
	/// </summary>
	class Ingesting {

		// OLD!!!!
		/// <summary>
		/// Function ingests filein CSV, filters all entries in column corresponding with comp according to
		/// replacement tuples (regex-match, replace), and outputs to fileout seperated by newlines
		/// </summary>
		/// <param name="filein">Input CSV File</param>
		/// <param name="comp">Name of column intended to ingest</param>
		/// <param name="filters">Array of string tuples where the first string is a regex match pattern,
		///	and the second string is what to replace the match with</param>
		/// <param name="fileout">Output file</param>
		public static void Ingest(string filein, string comp, Tuple<string, string>[] filters, string fileout) {
			using (TextFieldParser parser = new TextFieldParser(filein)) {
				parser.TextFieldType = FieldType.Delimited;
				parser.SetDelimiters(";");

				string[] fields;
				fields = parser.ReadFields(); // Parsing first line

				// Comment index
				uint com_pos = 0;

				// Search for corresponding column for comp
				foreach (string f in fields) {
					if (f == comp) break;
					++com_pos;
				}

				// Ensure we found what we were looking for (comp)
				if (com_pos == fields.Length) {
					// Didn't find comp (because we reached the end of the fields)
					Console.WriteLine("Compstring {0} not found.", comp);
					return;
				}

				Console.WriteLine("ingesting...");

				// Start ingesting, filtering, then regurgitating to output file line by line
				uint ind;
				string field;
				using (StreamWriter out_sw = new StreamWriter(fileout)) {
					while (!parser.EndOfData) {
						ind = 0;

						// Reads line by line
						fields = parser.ReadFields();

						// All fields in current line
						foreach (string f in fields) {
							field = f;

							// Ingest comment
							if (ind == com_pos) {
								// Ingest non-empty
								if (field != "") {
									// Filter things out (by replacing)
									foreach (var filter in filters) {
										field = Regex.Replace(field, filter.Item1, filter.Item2);
									}

									// Skip if message is wholly filtered out
									if (field == "") break;

									// Console.WriteLine(field); // output to be sure

									// Write out filtered text
									out_sw.WriteLine(field);
								}

								// No need to process any more fields on this line
								break;
							}

							ind++;
						}
					}
				}

				Console.WriteLine("done!");
			}
		}
		// OLD!!!!

		// OLD!!!!
		/// <summary>
		/// Function takes in filein file and builds a dictionary of unique words so as to compactly
		/// represent sentences as an array of dictionary indeces (array of ints). Words delimited by
		/// spaces, sentences delimited by newlines.
		/// </summary>
		/// <param name="filein">Input file, plain-text messages seperated by newlines</param>
		/// <returns>SentenceBank of dictionarized file</returns>
		public static SentenceBank Dictionarize(string filein) {
			// Idea is to ingest line by line, create array sized to fit each word of line
			// convert each word to corresponding unique dictionary index, fit into array
			// correspondingly, and add to ret's sentences list

			// Resultant dictionary and list of associated sentences
			List<string> dic = new List<string>();
			List<int[]> sent = new List<int[]>();

			using (StreamReader reader = new StreamReader(filein)) {
				Dictionary<string, int> wordmap = new Dictionary<string, int>();

				string curline;
				string[] words;

				int curindex;
				List<int> cursent;

				Console.WriteLine("dictionarizing...");

				while (!reader.EndOfStream) {
					// Reset working sentence list (gl garbage collection)
					cursent = new List<int>();

					curline = reader.ReadLine();

					// Chunk into words (by spaces)
					// TODO: in the future see if this can be replaced by some sort of stream construct
					words = curline.Split(' ');

					// Process each word
					foreach (string word in words) {
						if (!wordmap.TryGetValue(word, out curindex)) {
							// Value was not retrieved, word is unique!
							// Add to dictionary and all

							curindex = dic.Count();

							dic.Add(word);
							wordmap[word] = curindex;
						}

						// Current index into current sentence
						cursent.Add(curindex);
					}

					// Add end-of-message token at end
					cursent.Add(-1);

					// Convert current sentence to int array, add to sentences list
					sent.Add(cursent.ToArray());
				}

				Console.WriteLine("done!");
			}

			SentenceBank ret = new SentenceBank {
				dictionary = dic.ToArray(),
				sentences = sent.ToArray()
			};
			return ret;
		}
		// OLD!!!!

		// OLD!!!!
		/// <summary>
		/// Function writes provided bank to outfile
		/// </summary>
		/// <remarks>
		/// Encoding: JSON
		/// </remarks>
		/// <param name="outfile">Output filename</param>
		/// <param name="bank">Given sentence bank</param>
		public static void SaveBank(string outfile, SentenceBank bank) {
			var options = new JsonSerializerOptions {
				WriteIndented = false
			};

			Console.WriteLine("saving...");
			using (FileStream out_fstream = new FileStream(outfile, FileMode.Create)) {
				using (Utf8JsonWriter out_json_wr = new Utf8JsonWriter(out_fstream)) {
					JsonSerializer.Serialize<SentenceBank>(out_json_wr, bank, options);
				}
			}
			Console.WriteLine("done!");

			// Console.WriteLine(JsonSerializer.Serialize<SentenceBank>(bank, options));
		}
		// OLD!!!!

		// OLD!!!!
		/// <summary>
		/// Opens bank JSON and serializes it into a SentenceBank structure
		/// </summary>
		/// <param name="infile">Input filename</param>
		/// <returns></returns>
		public static SentenceBank OpenBank(string infile) {
			using (FileStream in_fstream = new FileStream(infile, FileMode.Open)) {
				return JsonSerializer.DeserializeAsync<SentenceBank>(in_fstream).Result;
			}
		}
		// OLD!!!!

		// Enums

		/// <summary>
		/// Enum for different stages of entire ingesting pipeline being finished
		/// </summary>
		public enum PipelineStages {
			none,
			csv_ingesting,
			filtering,
			dictionarizing,
			markovizing
		}

		/// <summary>
		/// Various states for ingesting, which 
		/// will sift up by end of the function
		/// </summary>
		public enum IngestStatus {
			ALL_GOOD,
			ERROR_COLUMN_NOT_FOUND
		}

		// Structures for ingesting

		/// <summary>
		/// Struct representing options for ingesting process
		/// </summary>
		public struct IngestOptions {
			/// <summary>
			/// Filename for input CSV file
			/// </summary>
			public string infile_csv;

			/// <summary>
			/// Name of column to match and pull comments from (default "Content")
			/// </summary>
			public string csv_column;

			/// <summary>
			/// Array of string pairs in the form of (reg, rep) where
			/// for each line ingested, the regex is matched by reg and
			/// replaced with rep in the order listed by the array.
			/// </summary>
			public Tuple<string, string>[] regex_filters;

			/// <summary>
			/// Threshold of total unique words for dictionarizer to perform a
			/// sweep (collects current dictionary words into master dictioanry)
			/// </summary>
			public ulong sweep_threshold;

			/// <summary>
			/// Filename for output dictionary file
			/// </summary>
			public string outfile_dictionary;

			/// <summary>
			/// Unsigned long representing size of n-gram for a markov chain segment
			/// </summary>
			public ulong gram_size;

			/// <summary>
			/// Filename for output markov file
			/// </summary>
			public string outfile_markov;
		}

		/// <summary>
		/// Master MarkovStructure, has associated dictioanry, array of links,
		/// and an array of indeces to links array which are all "starers"
		/// </summary>
		public struct MarkovStructure {
			/// <summary>
			/// Dictionary is an array of strings, where each index is used as the reference in the n-grams
			/// </summary>
			public string[] dictionary;

			/// <summary>
			/// Grams is an array of all possible unqiue grams on their own
			/// </summary>
			public NGram[] grams;

			/// <summary>
			/// Array of all unique ngrams, each with their successors
			/// </summary>
			public MarkovSegment[] chain_links;

			/// <summary>
			/// Array of indeces which point to chain links that happen to be starts of sentences
			/// </summary>
			public int[] seeds;
		}

		/// <summary>
		/// Single segment in overall MarkovStructure, used in tandem with master
		/// array to assemble sentence
		/// </summary>
		public struct MarkovSegment {
			/// <summary>
			/// Index which points to associated ngram in master
			/// markov structure
			/// </summary>
			public int current_ngram;

			/// <summary>
			/// Array of ngrams which succeed given ngram, along with their relatively frequency
			/// </summary>
			public NGramSuccessor[] successors;
		}

		/// <summary>
		/// Successor struct, couples ngram
		/// </summary>
		public struct NGramSuccessor {
			/// <summary>
			/// Index points to associated MarkovStructure chain_links index
			/// </summary>
			public int successor_index;

			/// <summary>
			/// Weight whos magnitude reflects relative frequency of successor
			/// </summary>
			public int weight;
		}

		/// <summary>
		/// Individual ngram
		/// </summary>
		public struct NGram {
			/// <summary>
			/// Array of indeces which correspond to words in dictionary
			/// </summary>
			public int[] gram;
		}

		/// <summary>
		/// Thread functions for ingesting
		/// </summary>
		public class Threads {
			/// <summary>
			/// Reads relevant column from input CSV file and
			/// sends to queue for next stage in pipeline; sets
			/// ready flag whenever finished
			/// </summary>
			/// <param name="options">Options for ingesting</param>
			/// <param name="outqueue_filter">Queue which function sends out ingested data from</param>
			/// <param name="ready">Ready flag, should be passed in as false, set to true at the end</param>
			public static bool CSV_Ingest(ref IngestOptions options, ref ConcurrentQueue<string> outqueue_filter,
										ref bool ready, out IngestStatus status) {
				status = IngestStatus.ALL_GOOD;

				using (TextFieldParser parser = new TextFieldParser(options.infile_csv)) {
					parser.TextFieldType = FieldType.Delimited;
					parser.SetDelimiters(";");

					// Read first row
					string[] fields;
					fields = parser.ReadFields();

					// Discover index for relevant column (options.csv_column)
					uint column_ind = 0;
					foreach(string f in fields) {
						if (f == options.csv_column) break;
						++column_ind;
					}

					// If no index discovered, return false, set status
					if(column_ind == fields.Length) {
						status = IngestStatus.ERROR_COLUMN_NOT_FOUND;
						return false;
					}

					// While not end of stream, read off specific column, push onto outqueue
				}

				ready = true;
				return true;
			}
		}

		/// <summary>
		/// Master ingesting function, pipelined to increase throughput
		/// </summary>
		/// <param name="options">Options struct for ingesting</param>
		public static bool IngestPipelined(ref IngestOptions options, out IngestStatus status) {
			status = IngestStatus.ALL_GOOD;

			ConcurrentQueue<string> conque_filter = new ConcurrentQueue<string>();
			bool csv_ready = false;
			// TODO: make this work (reorganize code, potentially some sort of
			// Pipeline object which has the necessary internal states (concurrentqueues),
			// and all that. it will be necesssary to figure out if and how to spawn multiple threads
			// for certain stages with this new orgnization
			Thread csv = new Thread(() => Threads.CSV_Ingest(ref options, ref conque_filter, ref csv_ready, out status));

			// ---Pipeline
			//	INPUT CSV --(INGESTING) --> RAW STRINGS --(FILTERING)--> LIST OF SENTENCE STRINGS --(DICTIONARIZING)-->
			//	--> SENTENCE BANK --(MARKOVIZING)--> MARKOV STRUCTURE

			// ---Structures
			//	SENTENCE BANK -- DICTIOANRY, SENTENCES
			//	MARKOV STRUCTURE -- DICTIONARY, MARKOV SEGMENTS
			//	MARKOV SEGMENT -- N-GRAM, N-GRAM SUCCESSOR
			//	N-GRAM SUCCESSOR -- N-GRAM, ASSOCIATED WEIGHT

			// ---Threads
			//	CSV Ingest thread --
			//		Reads relevant column from file and pushes into queue for filtering
			//		Finished flag :- End of CSV stream
			//
			//	Filtering thread(s) --
			//		until CSV Ingest is finished (known by flag),
			//		take one line and run through filters, then queue onto sentence string queue for dictionarizing
			//		Finished flag :- CSV Ingest is finished, Filtering queue is empty
			// 
			//	Filtering master thread --
			//		Launches all filtering threads
			//		Manages finished flag for filtering
			//		Finished flag :- all filtering threads are finished
			//
			//	Dictionarizing thread(s) --
			//		Each thread has a local word-cloud (hash table), word-list, and sentence list (queue)
			//		Dequeue a sentence string from queue, dictionarize to local cloud and word-list, push sentence to local list
			//		Local cloud and dictionary are ref parameters managed by master
			//		Finished flag :- Filtering is finished, sentence string queue is empty
			//
			//	Dictionarization master thread --
			//		Has master word-cloud, word-list, sentence queue for markovization
			//		For each thread, master has enumerator for its local word-list
			//		Launches all dictionarizing threads, supplying local clouds and lists
			//		Sweep --
			//			Monitors counts of each thread's dictionary counts as a current sum
			//			Once current sum is past some threshold, or functions are finished,
			//				Go through each thread's local list (starting at current enumerator)
			//				process into master dictioanry with master word cloud, increment enumerator until at end of local list
			//				Once all threads' lists have been processed, dequeue local sentences from each thread and
			//				enqueue into master sentence queue for markovization
			//		Finished flag :-	all dictionarization threads are themselves finished,
			//							their local lists have been processed into master dictioanry,
			//							master dictionary has been written out,
			//							all sentences from each local thread have been enqueued--
			//								--into master sentence queue for markovization
			//
			//	Markovizing thread(s) --
			//		Takes a dictionarized sentence off queue if available
			//		Markovizing sentence --
			//			Starting index at 1
			//			Continue flag set to true
			//			Declare current_gram
			//			Grab first gram:
			//			If sentence size is lte gram_size
			//				Ngram size is sentence size, grab available words, process and set continue flag to false
			//			Otherwsie grab gram_size, process into master dictionary, set as current_gram
			//			Put this first gram in seed list, if not there already
			//			Loop until continue flag is false --
			//				Grab new gram of gram_size in overlapping fashion (from index to index+3)
			//				If last word is -1, gram is finished, set continue to false
			//				TODO: CONTINUE!!!
			//
			//
			//
			//	Markovizing master thread -- 
			//		Has master unique ngram dictionary, maps ngram hash code to first-occurance ngram (hope to GOD no collisions)
			//			Consider simple function to check for equality
			//		Has master starter ngram dictionary, maps ngram to boolean true
			//		Has successor dictionary which maps an ngram to a counter dictionary, prototype to markovstructure struct
			//			Counter dictionary maps ngram to count, prototype to ngram-successor struct
			//		Launches all markovizing threads
			//		Whenever all markovization threads are finished--
			//			Create empty markovstructure, load dictionary
			//			enumerate through successor dictionary --
			//				TODO: CONTINUE!!!
			//			

			//	MARKOV STRUCTURE -- DICTIONARY, MARKOV SEGMENTS
			//	MARKOV SEGMENT -- N-GRAM, N-GRAM SUCCESSOR
			//	N-GRAM SUCCESSOR -- N-GRAM, ASSOCIATED WEIGHT

			return true;
		}
	}

	class Program {
		static void Main() {
			// TODO: proper options stuff

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
				sweep_threshold = 50, // TODO: change to like 1024 whenever doing the real thing
				outfile_dictionary = "test.dict",
				gram_size = 3,
				outfile_markov = "test.markov"
			};
			Ingesting.IngestPipelined(ref opts, out Ingesting.IngestStatus status);
		}
	}
}
