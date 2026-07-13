using Clyr.Cli;
using Clyr.Core;
using Clyr.Rules;
using Clyr.Windows;

var schemaPath = Path.Combine(Environment.CurrentDirectory, "rules", "schemas", "rule.schema.json");
if (!File.Exists(schemaPath))
{
    Console.Error.WriteLine("Rule schema was not found. Run CLYR from the repository root.");
    return 3;
}
var validator = new RuleValidator(File.ReadAllText(schemaPath));
var environment = new WindowsEnvironmentInfo();
var application = new CliApplication(environment, new DemoDataService(), validator, new PrivacyRedactor(environment), "CLYR 0.1.0-phase1");
return application.Run(args, Console.Out, Console.Error);
