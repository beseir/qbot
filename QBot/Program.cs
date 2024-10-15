using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using System.IO;
using Newtonsoft.Json;

namespace TelegramBotExperiments
{
    
    class Program
    {
        static ITelegramBotClient bot = new TelegramBotClient("7803273303:AAH6ouTDBwV4hzRAnJyCEdeQTBRxHeeS5N0");
        
        static Dictionary<long, Dictionary<string, Dictionary<string, int>>> chatSubjects = new();
        static Dictionary<long, Queue<string>> currentQueues = new();
        void LoadData()
        {
            if (System.IO.File.Exists("chatSubjects.json"))
            {
                string json = System.IO.File.ReadAllText("chatSubjects.json");
                chatSubjects = JsonConvert.DeserializeObject<Dictionary<long, Dictionary<string, Dictionary<string, int>>>>(json);
            }
        }
        void SaveData()
        {
            string json = JsonConvert.SerializeObject(chatSubjects);
            System.IO.File.WriteAllText("chatSubjects.json", json);
        }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
            {
                var message = update.Message;
                if (message?.Text == null) return;

                string messageText = message.Text;
                var chatId = message.Chat.Id;
                var userName = message.From.Username ?? "UnknownUser";

                if (messageText.StartsWith("/add subject ", StringComparison.OrdinalIgnoreCase))
                {
                    string subject = messageText.Substring("/add subject ".Length).Trim();
                    
                    if (!chatSubjects.ContainsKey(chatId))
                        chatSubjects[chatId] = new Dictionary<string, Dictionary<string, int>>();

                    if (!chatSubjects[chatId].ContainsKey(subject))
                    {
                        chatSubjects[chatId][subject] = new Dictionary<string, int>();
                        await botClient.SendTextMessageAsync(chatId, $"Предмет '{subject}' добавлен.");
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, $"Предмет '{subject}' уже есть.");
                    }
                    return;
                }

                if (messageText.StartsWith("/set ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = messageText.Split(' ');
                    if (parts.Length >= 4)
                    {
                        string subject = parts[1];
                        string user = parts[2].TrimStart('@');
                        if (int.TryParse(parts[3], out int labCount))
                        {
                            if (chatSubjects.ContainsKey(chatId) && chatSubjects[chatId].ContainsKey(subject))
                            {
                                if (chatSubjects[chatId][subject].ContainsKey(user))
                                    chatSubjects[chatId][subject][user] += labCount;
                                else
                                    chatSubjects[chatId][subject][user] = labCount;

                                await botClient.SendTextMessageAsync(chatId, $"у @{user} теперь {chatSubjects[chatId][subject][user]} лаб '{subject}'.");
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(chatId, $"Предмет '{subject}' не найден.");
                            }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "Ошибка.");
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, "нужно писать set ИмяПредмета @user число");
                    }
                    return;
                }

                if (messageText.StartsWith("/список ", StringComparison.OrdinalIgnoreCase))
                {
                    string subject = messageText.Substring("/список ".Length).Trim();
                    
                    if (chatSubjects.ContainsKey(chatId) && chatSubjects[chatId].ContainsKey(subject))
                    {
                        var queue = chatSubjects[chatId][subject];
                        if (queue.Count > 0)
                        {
                            string response = $"Список '{subject}':\n";
                            foreach (var entry in queue)
                            {
                                response += $"@{entry.Key}: {entry.Value} лаб\n";
                            }
                            await botClient.SendTextMessageAsync(chatId, response);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, $"Нет лаб по '{subject}'.");
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, $"Предмет '{subject}' не найден.");
                    }
                    return;
                }

                if (messageText.StartsWith("/start ", StringComparison.OrdinalIgnoreCase))
                {
                    string subject = messageText.Substring("/start ".Length).Trim();

                    if (chatSubjects.ContainsKey(chatId) && chatSubjects[chatId].ContainsKey(subject))
                    {
                        var queue = new Queue<string>(chatSubjects[chatId][subject]
                            .OrderBy(x => x.Value)
                            .ThenBy(x => x.Key)
                            .Select(x => x.Key));
                        
                        currentQueues[chatId] = queue;

                        if (queue.TryPeek(out string nextUser))
                        {
                            await botClient.SendTextMessageAsync(chatId, $"Начало очереди на '{subject}'. Первый идет @{nextUser}");
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, $"Нет людей на '{subject}'.");
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, $"Предмет '{subject}' не найден.");
                    }
                    return;
                }

                if (messageText.StartsWith("/skip", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = messageText.Split(' ');
                    int labIncrement = parts.Length > 1 && int.TryParse(parts[1], out int increment) ? increment : 0;

                    if (currentQueues.ContainsKey(chatId) && currentQueues[chatId].Count > 0)
                    {
                        var queue = currentQueues[chatId];
                        if (queue.TryDequeue(out string currentUser))
                        {
                            if (labIncrement > 0 && chatSubjects[chatId].Values.Any(subj => subj.ContainsKey(currentUser)))
                            {
                                foreach (var subject in chatSubjects[chatId].Values)
                                {
                                    if (subject.ContainsKey(currentUser))
                                    {
                                        subject[currentUser] += labIncrement;
                                        break;
                                    }
                                }
                            }

                            queue.Enqueue(currentUser);

                            if (queue.TryPeek(out string nextUser))
                            {
                                await botClient.SendTextMessageAsync(chatId, $"Следующий @{nextUser}");
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(chatId, $"Очередь пyстая.");
                            }
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, "Нечего скипать.");
                    }
                    return;
                }

                await botClient.SendTextMessageAsync(chatId, "не понял");
            }
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(exception));
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Bot started: " + bot.GetMeAsync().Result.FirstName);

            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { },
            };
            bot.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );

            Console.ReadLine();
        }
    }
}