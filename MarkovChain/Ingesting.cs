using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using System.Text.RegularExpressions; // regex
using Microsoft.VisualBasic.FileIO; // For CSV parsing

using MarkovChain.Structs;

/// <summary>
/// Ingesting namespace
/// </summary>
namespace MarkovChain.Ingesting {
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
		/// Unsigned long representing size of n-gram for a markov chain segment
		/// </summary>
		public int gram_size;

		/// <summary>
		/// Filename for output markov file
		/// </summary>
		public string outfile_markov;
	}

	/// <summary>
	/// Main object to facilitate concurrent pipelined ingesting
	/// </summary>
	public class Pipeline {
		// ---Pipeline
		//	INPUT CSV --(INGESTING) --> RAW STRINGS --(FILTERING)--> LIST OF SENTENCE STRINGS --(DICTIONARIZING)-->
		//	--> SENTENCE BANK --(MARKOVIZING)--> MARKOV STRUCTURE

		// Metaproces related fields
		public readonly IngestOptions options;
		public Status status;

		// Resultant values to grab
		public string[] master_dictionary;
		public MarkovStructure finished_markovstruct;

		// Concurrent queues for pipeline
		// set to private later
		public readonly ConcurrentQueue<string> conqueue_csv,
												conqueue_filtered;
		public readonly ConcurrentQueue<int[]> conqueue_dictionarized;

		// Flags
		private bool flag_csv,
						flag_filtered,
						flag_dictionarized;


		// Dictionarizing thread related constructs
		private readonly ConcurrentQueue<string> working_master_dictionary;
		private readonly ConcurrentDictionary<string, int> working_master_word_cloud;

		// Markovizing thread related constructs
		private readonly ConcurrentDictionary<NGram, int> working_master_ngram_cloud;

		private readonly ConcurrentQueue<NGram> working_master_ngrams;
		private readonly ConcurrentDictionary<int, bool> working_master_seeds;
		private readonly ConcurrentDictionary<int,
						ConcurrentDictionary<int, int>> working_master_successors;


		// Enums

		/// <summary>
		/// Various states for ingesting, which 
		/// will sift up by end of the function
		/// </summary>
		public enum Status {
			ALL_GOOD,
			ERROR_COLUMN_NOT_FOUND,
			INVALID_GRAM_SIZE
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="opt"></param>
		public Pipeline(IngestOptions opt) {
			options = opt;

			status = Status.ALL_GOOD;

			// Threads
			conqueue_csv = new ConcurrentQueue<string>();
			flag_csv = false;

			conqueue_filtered = new ConcurrentQueue<string>();
			flag_filtered = false;

			conqueue_dictionarized = new ConcurrentQueue<int[]>();
			flag_dictionarized = false;

			// Dictionarizing
			working_master_dictionary = new ConcurrentQueue<string>();
			working_master_word_cloud = new ConcurrentDictionary<string, int>();

			// Markovization
			working_master_ngram_cloud = new ConcurrentDictionary<NGram, int>();
			working_master_ngrams = new ConcurrentQueue<NGram>();
			working_master_seeds = new ConcurrentDictionary<int, bool>();
			working_master_successors = new ConcurrentDictionary<int, ConcurrentDictionary<int, int>>();
		}

		/// <summary>
		/// Master start function
		/// </summary>
		public bool Run() {
			// ---Pipeline
			//	INPUT CSV --(INGESTING) --> RAW STRINGS --(FILTERING)--> LIST OF SENTENCE STRINGS --(DICTIONARIZING)-->
			//	--> SENTENCE BANK --(MARKOVIZING)--> MARKOV STRUCTURE

			// ---Structures
			//	SENTENCE BANK -- DICTIOANRY, SENTENCES
			//	MARKOV STRUCTURE -- DICTIONARY, MARKOV SEGMENTS
			//	MARKOV SEGMENT -- N-GRAM, N-GRAM SUCCESSORS
			//	N-GRAM SUCCESSOR -- N-GRAM, ASSOCIATED WEIGHT

			// TODO: fancy output where each thread uses a callback function to write to specific console line
			//			-output in console size of all conqueues so for large data sets it can be seen progressing
			// TODO: consider lofting out each stage (and threads) into its own class for variable seperation

			// Gram size precondition
			if (options.gram_size < 1) {
				status = Status.INVALID_GRAM_SIZE;
				return false;
			}

			Thread thread_csv = new Thread(Thread_CSV_Ingest);
			Thread thread_filter = new Thread(Thread_Filter_Lead);
			Thread thread_dictionarize = new Thread(Thread_Dictionarize_Lead);
			Thread thread_markovize = new Thread(Thread_Markovize_Lead);

			thread_csv.Start();
			thread_filter.Start();
			thread_dictionarize.Start();
			thread_markovize.Start();

			thread_csv.Join();
			thread_filter.Join();
			thread_dictionarize.Join();
			thread_markovize.Join();

			return status == Status.ALL_GOOD;
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

			// TODO: consider switching to local queues which leading thread populates some day (may improve concurrency?)

			Console.WriteLine("[Filter #{0}]: Starting...", id);

			// Stop :- csv_ingest_finihed, conqueue_csv.IsEmpty
			while (!(flag_csv && conqueue_csv.IsEmpty)) {
				if (conqueue_csv.TryDequeue(out string piece)) {
					// Take piece out, run through filters, enqueue if applicable
					foreach (var filter in options.regex_filters) {
						piece = Regex.Replace(piece, filter.Item1, filter.Item2);

						// No bother filtering if string is already empty
						if (piece == "") break;
					}

					// Skip enqueuing string is empty
					if (piece == "") continue;

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

			// Launch threads
			Task[] workers = new Task[concur];

			Console.WriteLine("[Dictionarize Lead]: Dispatching {0} workers...", concur);

			for (int i = 0; i < concur; ++i) {
				workers[i] = Task.Run(() => Thread_Dictionarize_Work(i));
			}

			Task.WaitAll(workers);

			Console.WriteLine("[Dictionarize Lead]: Workers finished!");

			// Transform working master dictionary to final master dictionary
			master_dictionary = working_master_dictionary.ToArray();

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
						if (!working_master_word_cloud.TryGetValue(word, out int index)) {
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
			//	Markovizing master thread -- 
			//		Has master ngrams collection, concurrentqueue of ngrams which will be referred by indeces in other vars
			//		Has master ngram seed collection, concurrent bag of integers which point to indeces
			//		Has successor dictionary which maps an index to a counting dictionary -- prototype to markovstructure struct
			//			Counter dictionary maps index to count -- prototype to ngram-successor struct
			//		Launches all markovizing threads
			//		Whenever all markovization threads are finished-- construct MarkovStructure with structures

			int concur = 1; // TOOD: put this into options some day

			// Launch threads
			Task[] workers = new Task[concur];

			Console.WriteLine("[Markovization Lead]: Dispatching {0} workers...", concur);

			for (int i = 0; i < concur; ++i) {
				workers[i] = Task.Run(() => Thread_Markovize_Work(i));
			}

			Task.WaitAll(workers);

			Console.WriteLine("[Markovization Lead]: Workers finished!");

			// Create finished markovization product
			finished_markovstruct = new MarkovStructure(master_dictionary, working_master_ngrams,
										working_master_successors, working_master_seeds);
		}

		private void Thread_Markovize_Work(int id) {
			//	Markovizing thread(s) --
			//		Takes a dictionarized sentence off queue if available
			//		Markovizing sentence --
			//			Starting index at 1
			//			Declare current gram, new gram
			//			Grab first gram:
			//			If sentence size is lte gram size
			//				current gram size is sentence size, grab available words, process into current gram
			//			Otherwsie grab gram size, set as current gram
			//			Put this first gram in seed list, if not there already
			//			Loop until positioned where gram size grabs last word (pos < len-size) --
			//				Grab new gram of gram size in overlapping fashion (from index to index+gram size)
			//				In current grams successor's, incremeent count pointed to by new gram
			//				If no count exists (new successor), set count pointed to by new gram to 1
			//				Set current gram = new gram
			//				Increment index
			//		Finished flag :- dictionarized conqueue is empty, dictionarization flag is true

			while (!conqueue_dictionarized.IsEmpty || !flag_dictionarized) {
				int pos, // position along sentence
					index, // index of current ngram
					index_suc; // index of succeeding ngram

				NGram curgram;

				if (conqueue_dictionarized.TryDequeue(out int[] cursent)) {
					pos = 0;

					// Grab firs gram

					// Short-circuit procedure for when sentence is to be made up of one gram
					if (cursent.Length <= options.gram_size) {
						// Sentence is one gram which may or may not be short
						curgram = new NGram(cursent);

						// Register, set as seed
						working_master_seeds[Markovization_Register_Gram(ref curgram)] = true;

						// Move on with next sentence
						continue;
					}

					//Regular procedure, grabs first gram, regisers, and loops to the end grabbing grams
					curgram = Markovization_Ingest_Gram(cursent, pos++);
					index = Markovization_Register_Gram(ref curgram);

					// Register as seed
					working_master_seeds[index] = true;

					//	In cases where sentence is made of more than 1 gram
					//		visualization, length is 6, gram-size is 3:
					//			0 1 2 3 4 -1
					//			      ^stop
					//		pos <= 6[length] - 3[size]
					while (pos <= cursent.Length - options.gram_size) {
						curgram = Markovization_Ingest_Gram(cursent, pos++);
						index_suc = Markovization_Register_Gram(ref curgram);

						// Update (or establish) successor counter
						if (working_master_successors[index].ContainsKey(index_suc)) {
							working_master_successors[index][index_suc] = working_master_successors[index][index_suc] + 1;
						} else {
							working_master_successors[index][index_suc] = 1;
						}

						// Shift out the old
						index = index_suc;
					}
				} else {
					Thread.Yield();
				}
			}
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

		/// <summary>
		/// Ingests NGram
		/// </summary>
		/// <param name="cursent"></param>
		/// <param name="position"></param>
		/// <returns></returns>
		private NGram Markovization_Ingest_Gram(int[] cursent, int position) {
			int[] proto_gram = new int[options.gram_size];
			Array.Copy(cursent, position, proto_gram, 0, options.gram_size);
			return new NGram(proto_gram);
		}

		/// <summary>
		/// Add NGram to master structure, return its index
		/// </summary>
		/// <param name="gram"></param>
		/// <returns></returns>
		private int Markovization_Register_Gram(ref NGram gram) {
			int index;
			// Get corresponding index of first
			if (!working_master_ngram_cloud.TryGetValue(gram, out index)) {
				// Gram is unique as of yet
				index = working_master_ngrams.Count();

				// Put in list, get index
				working_master_ngrams.Enqueue(gram);
				working_master_ngram_cloud[gram] = index;

				// Create new successors dictioanry
				working_master_successors[index] = new ConcurrentDictionary<int, int>();
			}

			return index;
		}
	}
}
