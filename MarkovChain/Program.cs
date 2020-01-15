using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using System.Text.RegularExpressions; // regex
using Microsoft.VisualBasic.FileIO; // For CSV parsing
using System.IO; // StreamWriter, StreamReader
using System.Text.Json; // JSON

namespace MarkovChain {
	public static class Utils {
		/// <summary>
		/// Sweep over text
		/// </summary>
		/// <param name="Text"></param>
		/// <returns></returns>
		/// <remarks>https://stackoverflow.com/a/1443004</remarks>
		public static IEnumerable<string> WordList(this string Text) {
			int cIndex = 0;
			int nIndex;
			while ((nIndex = Text.IndexOf(' ', cIndex + 1)) != -1) {
				int sIndex = (cIndex == 0 ? 0 : cIndex + 1);
				yield return Text.Substring(sIndex, nIndex - sIndex);
				cIndex = nIndex;
			}
			yield return Text.Substring(cIndex + 1);
		}
	}

	/// <summary>
	/// Class holds several functions for processing data.
	/// </summary>
	class Ingesting {

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
		/// Main object to facilitate concurrent pipelined ingesting
		/// </summary>
		public class Pipeline {
			// ---Pipeline
			//	INPUT CSV --(INGESTING) --> RAW STRINGS --(FILTERING)--> LIST OF SENTENCE STRINGS --(DICTIONARIZING)-->
			//	--> SENTENCE BANK --(MARKOVIZING)--> MARKOV STRUCTURE

			// Metaproces related fields
			public IngestOptions options;
			public Stages stage;
			public Status status;

			// Concurrent queues for pipeline
			// set to private later
			public ConcurrentQueue<string> conqueue_csv,
												conqueue_filtered;
			public ConcurrentQueue<int[]> conqueue_dictionarized;

			// Flags
			private bool flag_csv,
							flag_filtered,
							flag_dictionarized;


			// Dictionarizing thread related constructs
			private ConcurrentQueue<string> working_master_dictionary;
			private ConcurrentDictionary<string, int> working_master_word_cloud;

			// Master dictionary bank
			string[] master_dictionary;

			// Enums

			/// <summary>
			/// Enum for different stages of entire ingesting pipeline being finished
			/// </summary>
			public enum Stages {
				csv_ingesting,
				filtering,
				dictionarizing,
				markovizing,
				finished
			}

			/// <summary>
			/// Various states for ingesting, which 
			/// will sift up by end of the function
			/// </summary>
			public enum Status {
				ALL_GOOD,
				ERROR_COLUMN_NOT_FOUND
			}

			/// <summary>
			/// Constructor
			/// </summary>
			/// <param name="opt"></param>
			public Pipeline(IngestOptions opt) {
				options = opt;

				stage = Stages.csv_ingesting;
				status = Status.ALL_GOOD;

				// Threads
				conqueue_csv = new ConcurrentQueue<string>();
				flag_csv = false;

				conqueue_filtered = new ConcurrentQueue<string>();
				flag_filtered = false;

				conqueue_dictionarized = new ConcurrentQueue<int[]>();
				flag_dictionarized = false;
			}

			/// <summary>
			/// Master start function
			/// </summary>
			public void Start() {
				Thread thread_csv = new Thread(Thread_CSV_Ingest);
				Thread thread_filter = new Thread(Thread_Filter_Lead);
				Thread thread_dictionarize = new Thread(Thread_Dictionarize_Lead);

				thread_csv.Start();
				thread_filter.Start();
				thread_dictionarize.Start();

				thread_csv.Join();
				thread_filter.Join();
				thread_dictionarize.Join();

				// TODO: start rest of threads
				// TODO: make use of stage variable (maybe it can go in place of flags?)
			}

			// Threads

			/// <summary>
			/// CSV Ingesting stage master thread
			/// </summary>
			private void Thread_CSV_Ingest() {
				Console.WriteLine("[CSV]: Starting...");
				using (TextFieldParser parser = new TextFieldParser(options.infile_csv)) {
					parser.TextFieldType = FieldType.Delimited;
					parser.SetDelimiters(";");

					// Read first row
					string[] fields;
					fields = parser.ReadFields();

					// Discover index for relevant column (options.csv_column)
					uint column_ind = 0;
					foreach (string f in fields) {
						if (f == options.csv_column) break;
						++column_ind;
					}

					// If no index discovered, failure
					if (column_ind == fields.Length) {
						status = Status.ERROR_COLUMN_NOT_FOUND;
						Failure_Callback();
						return;
					}

					// While not end of stream, read off specific column, push onto filter queue
					while (!parser.EndOfData) {

						fields = parser.ReadFields();
						string msg = fields[column_ind];

						if (msg != "") conqueue_csv.Enqueue(msg);
					}
				}

				Console.WriteLine("[CSV]: Finished!");
				flag_csv = true;
			}

			/// <summary>
			/// Filtering leader thread. Launches filtering work
			/// thread(s), manages finished flag for filtering
			/// </summary>
			/// <remarks>Finished flag :- all filtering thread(s) are finished</remarks>
			private void Thread_Filter_Lead() {
				//	Filtering master thread --
				//		Launches all filtering threads
				//		Manages finished flag for filtering
				//		Finished flag :- all filtering threads are finished

				int concur = 1; // TODO: put this into options

				Task[] workers = new Task[concur];

				Console.WriteLine("[Filter Lead]: Dispatching {0} workers...", concur);

				for (int i = 0; i < concur; ++i) {
					workers[i] = Task.Run(() => Thread_Filter_Work(i));
				}

				Task.WaitAll(workers);

				Console.WriteLine("[Filter Lead]: Workers finished!");

				flag_filtered = true;
			}

			/// <summary>
			/// Filtering work thread(s).
			/// </summary>
			private void Thread_Filter_Work(int id) {
				//	Filtering thread(s) --
				//		until CSV Ingest is finished (known by flag),
				//		take one line and run through filters, then queue onto sentence string queue for dictionarizing
				//		Finished flag :- CSV Ingest is finished, Filtering queue is empty

				// TODO: Switch to local queues which leading thread fills some day (may improve concurrency)

				Console.WriteLine("[Filter #{0}]: Starting...", id);

				// Stop :- csv_ingest_finihed, conqueue_csv.IsEmpty
				while (!(flag_csv && conqueue_csv.IsEmpty)) {
					if (conqueue_csv.TryDequeue(out string piece)) {
						// Take piece out, run through filters, enqueue if applicable
						foreach (var filter in options.regex_filters) {
							// Console.WriteLine("[Filter #{0}]: Pulling from CSV queue...", id);

							piece = Regex.Replace(piece, filter.Item1, filter.Item2);

							// No bother filtering if string is already empty
							if (piece == "") break;
						}

						// Skip enqueuing string is empty
						if (piece == "") continue;

						// Console.WriteLine("[Filter #{0}]: Enqueuing piece...", id);

						conqueue_filtered.Enqueue(piece);
					} else {
						// me guess is csv finished flag is still false, queue is empty waiting to be filled
						// very slim chance flag is true, and there was a small race condition between
						// entering the while and pulling
						Thread.Yield();
					}
				}

				Console.WriteLine("[Filter #{0}]: Finished!", id);
			}

			private void Thread_Dictionarize_Lead() {
				//	Dictionarization master thread --
				//		Has master word-cloud, word-list, sentence queue for markovization
				//		Finished flag :-	all dictionarization threads are themselves finished,
				//							their local lists have been processed into master dictioanry,
				//							master dictionary has been written out,
				//							all sentences from each local thread have been enqueued--
				//								--into master sentence queue for markovization

				int concur = 1; // TODO: put this into options someday

				// Master word cloud, master word list
				working_master_dictionary = new ConcurrentQueue<string>();
				working_master_word_cloud = new ConcurrentDictionary<string, int>();
				
				// Launch threads
				Task[] workers = new Task[concur];

				Console.WriteLine("[Dictionarize Lead]: Dispatching {0} workers...", concur);

				for(int i = 0; i < concur; ++i) {
					workers[i] = Task.Run(() => Thread_Dictionarize_Work(i));
				}

				Task.WaitAll(workers);

				Console.WriteLine("[Dictionarize Lead]: Workers finished!");

				// Transform working master dictionary to final master dictionary
				master_dictionary = working_master_dictionary.ToArray();

				// Write master dictioanry out
				// Use streamize writing so as to prevent excess memory usage
				using (StreamWriter sw = new StreamWriter(options.outfile_dictionary)) {
					foreach (string word in master_dictionary) {
						sw.WriteLine(word);
					}
				}
				// TODO: decide on whether to throw out master dictionary (not needed at this point)

				flag_dictionarized = true;
			}

			private void Thread_Dictionarize_Work(int id) {
				//	Dictionarizing thread(s) --
				//		Dequeue a sentence string from preceeding filtered queue
				//		construct dictionarize w/ master cloud and master list
				//		push dictionarized sentence to conqueue
				//		Finished flag :- Filtering is finished, filtered strings queue is empty

				Console.WriteLine("[Dictionarize #{0}]: Starting...", id);

				while (!flag_filtered || !conqueue_filtered.IsEmpty) {
					List<int> cursent; // current dictionarized sentence

					if (conqueue_filtered.TryDequeue(out string sentence)) {
						// We have a sentence, dictionarize each word
						cursent = new List<int>();

						// Dictionarize
						foreach (string word in sentence.WordList()) {
							int index;
							if (!working_master_word_cloud.TryGetValue(word, out index)) {
								// There is no index for the current word, we must add one
								index = working_master_dictionary.Count();

								working_master_dictionary.Enqueue(word);
								working_master_word_cloud[word] = index;
							} // else, the trygetvalue succeeded, we have an index (no further action necessary)

							cursent.Add(index);
						}

						// Add "end of sentence" symbol
						cursent.Add(-1);

						// Enqueue array onto conqueue
						conqueue_dictionarized.Enqueue(cursent.ToArray());
					} else {
						// waiting on queue to be filled
						Thread.Yield();
					}
				}

				Console.WriteLine("[Dictionarize #{0}]: Finished!", id);
			}

			private void Thread_Markovize_Lead() {

			}

			private void Thread_Markovize_Work(int id) {

			}

			/// <summary>
			/// Failure callback function, executes whenver there is some sort of failure.
			/// </summary>
			private void Failure_Callback() {
				Console.WriteLine("Failure has occured!");
				switch (status) {
					case Status.ERROR_COLUMN_NOT_FOUND:
						Console.WriteLine("Column {0} not found in {1}!", options.csv_column, options.infile_csv);
						break;
				}
			}
		}

		/// <summary>
		/// Master ingesting function, pipelined to increase throughput
		/// </summary>
		/// <param name="options">Options struct for ingesting</param>
		public static bool IngestPipelined(ref IngestOptions options, out Pipeline.Status status) {
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

			Pipeline pipe = new Pipeline(options);
			pipe.Start();
			status = pipe.status;
			return status == Pipeline.Status.ALL_GOOD;
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
				outfile_dictionary = "test.dict",
				gram_size = 3,
				outfile_markov = "test.markov"
			};
			if (!Ingesting.IngestPipelined(ref opts, out Ingesting.Pipeline.Status status)) Console.WriteLine("Some sort of error occured.");
		}
	}
}
