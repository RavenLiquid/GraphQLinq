﻿using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace GraphQLinq.Scaffolding
{
    class Program
    {
        private const string IntrospectionQuery = @"{
  __schema {
    types {
      name
      interfaces {
        name
      }
      description
      kind
      enumValues {
        name
      }
      description
      fields {
        name
        description
        type {
          name
          kind
          ofType {
            name
            kind
            ofType {
              name
              kind
              ofType {
                name
                kind
                ofType {
                  name
                  kind
                  ofType {
                    name
                    kind
                  }
                }
              }
            }
          }
        }
        args {
          name
          description
          type {
            kind
            name
            description
            ofType {
              name
              kind
              ofType {
                name
                kind
                ofType {
                  name
                  kind
                }
              }
            }
          }
        }
      }
      inputFields {
        name
        description
        type {
          name
          kind
          ofType {
            name
            kind
            ofType {
              name
              kind
              ofType {
                name
                kind
              }
            }
          }
        }
      }
    }
    queryType {
      name
    }
    mutationType {
      name
    }
    subscriptionType {
      name
    }
  }
}
";

        private static async Task Main(string[] args)
        {
            var generate = new RootCommand
            {
                new Argument<string>("endpoint", "Endpoint of the GraphQL service"),
                new Option<string>(new []{ "--output", "-o" }, "Output folder"),
                new Option<string>(new []{ "--namespace", "-n" }, "Namespace of generated classes"),
                new Option<string>(new []{ "--context", "-c" }, "Name of the generated context classes"),
            };

            generate.Handler = CommandHandler.Create<string, string, string, string, IConsole>(HandleGenerate);

            await generate.InvokeAsync(args);
        }

        private static async Task HandleGenerate(string endpoint, string? output, string? @namespace, string? context, IConsole console)
        {
            //var webClient = new WebClient();
            //webClient.Headers.Add("Content-Type", "application/json");
            //var downloadString = webClient.UploadString("endpoint", query);
            Thread.Sleep(5000);
            AnsiConsole.MarkupLine("[bold]Welcome to GraphQL Client Scaffolding tool[/]");
            AnsiConsole.WriteLine();

            string outputFolder = Path.IsPathRooted(output) ? output : Path.Combine(Environment.CurrentDirectory, output ?? "");

            AnsiConsole.MarkupLine("Scaffolding GraphQL client code for [bold]{0}[/] to [bold]{1}[/]", endpoint, outputFolder);

            var schema = await AnsiConsole.Status().StartAsync("Performing introspection", async ctx =>
            {
                AnsiConsole.WriteLine("Running introspection query ...");
                using var httpClient = new HttpClient();
                using var responseMessage = await httpClient.PostAsJsonAsync(endpoint, new { query = IntrospectionQuery });

                AnsiConsole.WriteLine("Reading and deserializing schema information ...");
                var schemaJson = await responseMessage.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<RootSchemaObject>(schemaJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });
            AnsiConsole.WriteLine();

            var contextClassFullName = AnsiConsole.Status().Start($"Scaffolding GraphQL client code {endpoint}", statusContext =>
            {
                var codeGenerationOptions = new CodeGenerationOptions
                {
                    Namespace = @namespace ?? "",
                    NormalizeCasing = true,
                    OutputDirectory = outputFolder,
                    ContextName = context ?? "Query"
                };

                var graphQLClassesGenerator = new GraphQLClassesGenerator(codeGenerationOptions);
                return graphQLClassesGenerator.GenerateClient(schema.Data.Schema, endpoint);
            });

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Scaffolding complete[/]");
            AnsiConsole.MarkupLine("Use [bold]{0}[/] to run strongly typed LINQ queries", contextClassFullName);

            Console.ReadKey();
        }
    }

    class CodeGenerationOptions
    {
        public string? Namespace { get; set; } = "";
        public string ContextName { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
        public bool NormalizeCasing { get; set; }
    }
}