using System.Globalization;
using System.Text;
using MyApp.ServiceModel;
using ServiceStack;

namespace MyApp.ServiceInterface;

public class UnderwritingServices : Service
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, QuestionDefinition> Questions = [];
    private static readonly Dictionary<string, QuoteRule> Rules = [];
    private static readonly Dictionary<string, ClientRecord> Clients = [];
    private static readonly Dictionary<string, ApplicationRecord> Applications = [];
    private static readonly Dictionary<string, PolicyRecord> Policies = [];

    static UnderwritingServices() => SeedDefaults();

    public object Get(ManageQuestions request)
    {
        lock (SyncRoot)
        {
            return new QuestionsResponse
            {
                Items = Questions.Values.OrderBy(x => x.Key).ToList()
            };
        }
    }

    public object Post(ManageQuestions request)
    {
        var question = request.Question;
        if (question is null || question.Key.IsNullOrEmpty())
            throw HttpError.BadRequest("Question key is required");

        lock (SyncRoot)
        {
            Questions[question.Key] = NormalizeQuestion(question);
            return new QuestionsResponse
            {
                Items = Questions.Values.OrderBy(x => x.Key).ToList()
            };
        }
    }

    public object Get(ManageRules request)
    {
        lock (SyncRoot)
        {
            return new RulesResponse
            {
                Items = Rules.Values.OrderBy(x => x.Id).ToList()
            };
        }
    }

    public object Post(ManageRules request)
    {
        var rule = request.Rule;
        if (rule is null || rule.Id.IsNullOrEmpty())
            throw HttpError.BadRequest("Rule id is required");

        lock (SyncRoot)
        {
            Rules[rule.Id] = NormalizeRule(rule);
            return new RulesResponse
            {
                Items = Rules.Values.OrderBy(x => x.Id).ToList()
            };
        }
    }

    public object Get(ManageClients request)
    {
        lock (SyncRoot)
        {
            return new ClientsResponse
            {
                Items = Clients.Values.OrderBy(x => x.Name).ToList(),
            };
        }
    }

    public object Post(ManageClients request)
    {
        var name = request.Name?.Trim();
        if (name.IsNullOrEmpty())
            throw HttpError.BadRequest("Client name is required");

        var client = new ClientRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name!,
        };

        lock (SyncRoot)
        {
            Clients[client.Id] = client;
            return new ClientsResponse
            {
                Created = client,
                Items = Clients.Values.OrderBy(x => x.Name).ToList(),
            };
        }
    }

    public object Get(ManageApplications request)
    {
        lock (SyncRoot)
        {
            return new ApplicationsResponse
            {
                Items = Applications.Values.OrderByDescending(x => x.CreatedAtUtc).ToList(),
            };
        }
    }

    public object Post(ManageApplications request)
    {
        var clientId = request.ClientId?.Trim();
        var productCode = request.ProductCode?.Trim();

        if (clientId.IsNullOrEmpty())
            throw HttpError.BadRequest("A valid client is required");

        if (productCode.IsNullOrEmpty())
            throw HttpError.BadRequest("Product code is required");

        lock (SyncRoot)
        {
            if (!Clients.ContainsKey(clientId!))
                throw HttpError.BadRequest("A valid client is required");

            var application = new ApplicationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                ClientId = clientId!,
                ProductCode = productCode!,
                Answers = request.Answers ?? [],
                CreatedAtUtc = DateTime.UtcNow,
            };

            Applications[application.Id] = application;
            return new ApplicationsResponse
            {
                Created = application,
                Items = Applications.Values.OrderByDescending(x => x.CreatedAtUtc).ToList(),
            };
        }
    }

    public object Post(QuoteApplication request)
    {
        lock (SyncRoot)
        {
            var app = GetApplicationOrThrow(request.ApplicationId);
            return EvaluateQuote(app);
        }
    }

    public object Post(BindPolicy request)
    {
        lock (SyncRoot)
        {
            var app = GetApplicationOrThrow(request.ApplicationId);
            var quote = EvaluateQuote(app);

            var policy = new PolicyRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                ApplicationId = app.Id,
                PolicyNumber = $"POL-{DateTime.UtcNow:yyyyMMdd}-{Policies.Count + 1:0000}",
                BoundAtUtc = DateTime.UtcNow,
                Quote = quote,
            };

            Policies[policy.Id] = policy;
            return policy;
        }
    }

    public object Get(ManagePolicies request)
    {
        lock (SyncRoot)
        {
            return new PoliciesResponse
            {
                Items = Policies.Values.OrderByDescending(x => x.BoundAtUtc).ToList(),
            };
        }
    }

    public object Get(GetBordereau request)
    {
        lock (SyncRoot)
        {
            var rows = Policies.Values
                .OrderByDescending(x => x.BoundAtUtc)
                .Select(policy =>
                {
                    var app = Applications[policy.ApplicationId];
                    var clientName = Clients.TryGetValue(app.ClientId, out var client) ? client.Name : "Unknown";
                    return new BordereauRow
                    {
                        PolicyNumber = policy.PolicyNumber,
                        ClientName = clientName,
                        ProductCode = app.ProductCode,
                        BoundAtUtc = policy.BoundAtUtc,
                        Endorsements = string.Join(";", policy.Quote.Endorsements.OrderBy(x => x)),
                        Limits = ToKeyValueString(policy.Quote.Limits),
                        Deductibles = ToKeyValueString(policy.Quote.Deductibles),
                    };
                })
                .ToList();

            return new BordereauResponse
            {
                Rows = rows,
                Csv = ToCsv(rows),
            };
        }
    }

    private static ApplicationRecord GetApplicationOrThrow(string applicationId)
    {
        if (applicationId.IsNullOrEmpty() || !Applications.TryGetValue(applicationId, out var application))
            throw HttpError.NotFound("Application was not found");
        return application;
    }

    private static QuoteResponse EvaluateQuote(ApplicationRecord application)
    {
        var quote = new QuoteResponse();

        foreach (var rule in Rules.Values.OrderBy(x => x.Id))
        {
            if (!MatchesRule(rule, application.Answers))
                continue;

            foreach (var action in rule.Actions)
            {
                switch (action.Type)
                {
                    case RuleActionType.AddEndorsement when !action.Name.IsNullOrEmpty() && !quote.Endorsements.Contains(action.Name):
                        quote.Endorsements.Add(action.Name);
                        break;
                    case RuleActionType.SetLimit when !action.Name.IsNullOrEmpty() && action.NumericValue.HasValue:
                        quote.Limits[action.Name] = action.NumericValue.Value;
                        break;
                    case RuleActionType.SetDeductible when !action.Name.IsNullOrEmpty() && action.NumericValue.HasValue:
                        quote.Deductibles[action.Name] = action.NumericValue.Value;
                        break;
                }
            }
        }

        quote.Endorsements = quote.Endorsements.OrderBy(x => x).ToList();
        return quote;
    }

    private static bool MatchesRule(QuoteRule rule, Dictionary<string, string> answers)
    {
        foreach (var condition in rule.Conditions)
        {
            if (!Questions.TryGetValue(condition.QuestionKey, out var question))
                return false;

            answers.TryGetValue(condition.QuestionKey, out var answer);
            answer ??= string.Empty;

            if (!MatchesCondition(question, condition, answer))
                return false;
        }

        return true;
    }

    private static bool MatchesCondition(QuestionDefinition question, RuleCondition condition, string answer)
    {
        return question.Type switch
        {
            ApplicationQuestionType.Number => CompareNumber(condition, answer),
            ApplicationQuestionType.Date => CompareDate(condition, answer),
            ApplicationQuestionType.Selection => CompareSelection(condition, answer),
            _ => false,
        };
    }

    private static bool CompareNumber(RuleCondition condition, string answer)
    {
        if (!decimal.TryParse(answer, NumberStyles.Number, CultureInfo.InvariantCulture, out var actual) ||
            !decimal.TryParse(condition.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
            return false;

        return condition.Operator switch
        {
            RuleConditionOperator.Equals => actual == expected,
            RuleConditionOperator.NotEquals => actual != expected,
            RuleConditionOperator.GreaterThan => actual > expected,
            RuleConditionOperator.GreaterThanOrEqual => actual >= expected,
            RuleConditionOperator.LessThan => actual < expected,
            RuleConditionOperator.LessThanOrEqual => actual <= expected,
            _ => false,
        };
    }

    private static bool CompareDate(RuleCondition condition, string answer)
    {
        if (!DateTime.TryParse(answer, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var actual) ||
            !DateTime.TryParse(condition.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expected))
            return false;

        actual = actual.Date;
        expected = expected.Date;

        return condition.Operator switch
        {
            RuleConditionOperator.Equals => actual == expected,
            RuleConditionOperator.NotEquals => actual != expected,
            RuleConditionOperator.Before => actual < expected,
            RuleConditionOperator.After => actual > expected,
            RuleConditionOperator.GreaterThan => actual > expected,
            RuleConditionOperator.GreaterThanOrEqual => actual >= expected,
            RuleConditionOperator.LessThan => actual < expected,
            RuleConditionOperator.LessThanOrEqual => actual <= expected,
            _ => false,
        };
    }

    private static bool CompareSelection(RuleCondition condition, string answer)
    {
        var actual = answer.Trim();
        var expected = condition.Value.Trim();

        return condition.Operator switch
        {
            RuleConditionOperator.Equals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            RuleConditionOperator.NotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            RuleConditionOperator.In => expected
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(x => string.Equals(actual, x, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static QuestionDefinition NormalizeQuestion(QuestionDefinition question)
    {
        question.Key = question.Key.Trim();
        question.Label = question.Label.Trim();
        question.Options = (question.Options ?? [])
            .Where(x => !x.IsNullOrEmpty())
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return question;
    }

    private static QuoteRule NormalizeRule(QuoteRule rule)
    {
        rule.Id = rule.Id.Trim();
        rule.Name = rule.Name.Trim();
        rule.Conditions = (rule.Conditions ?? [])
            .Where(x => !x.QuestionKey.IsNullOrEmpty())
            .Select(x => new RuleCondition
            {
                QuestionKey = x.QuestionKey.Trim(),
                Operator = x.Operator,
                Value = x.Value?.Trim() ?? string.Empty,
            })
            .ToList();
        rule.Actions = (rule.Actions ?? [])
            .Where(x => !x.Name.IsNullOrEmpty())
            .Select(x => new RuleAction
            {
                Type = x.Type,
                Name = x.Name.Trim(),
                NumericValue = x.NumericValue,
            })
            .ToList();
        return rule;
    }

    private static string ToKeyValueString(Dictionary<string, decimal> values) =>
        string.Join(";", values.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value.ToString(CultureInfo.InvariantCulture)}"));

    private static string ToCsv(List<BordereauRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PolicyNumber,ClientName,ProductCode,BoundAtUtc,Endorsements,Limits,Deductibles");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', [
                EscapeCsv(row.PolicyNumber),
                EscapeCsv(row.ClientName),
                EscapeCsv(row.ProductCode),
                EscapeCsv(row.BoundAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                EscapeCsv(row.Endorsements),
                EscapeCsv(row.Limits),
                EscapeCsv(row.Deductibles),
            ]));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static void SeedDefaults()
    {
        if (Questions.Count > 0 || Rules.Count > 0)
            return;

        Questions["sum_insured"] = new QuestionDefinition
        {
            Key = "sum_insured",
            Label = "Sum Insured",
            Type = ApplicationQuestionType.Number,
        };
        Questions["effective_date"] = new QuestionDefinition
        {
            Key = "effective_date",
            Label = "Effective Date",
            Type = ApplicationQuestionType.Date,
        };
        Questions["industry"] = new QuestionDefinition
        {
            Key = "industry",
            Label = "Industry",
            Type = ApplicationQuestionType.Selection,
            Options = ["Retail", "Manufacturing", "Technology"],
        };

        Rules["default_limit"] = new QuoteRule
        {
            Id = "default_limit",
            Name = "Base limit from sum insured",
            Conditions =
            [
                new RuleCondition
                {
                    QuestionKey = "sum_insured",
                    Operator = RuleConditionOperator.GreaterThanOrEqual,
                    Value = "100000"
                }
            ],
            Actions =
            [
                new RuleAction
                {
                    Type = RuleActionType.SetLimit,
                    Name = "Property",
                    NumericValue = 100000,
                },
                new RuleAction
                {
                    Type = RuleActionType.SetDeductible,
                    Name = "Property",
                    NumericValue = 2500,
                }
            ]
        };

        Rules["tech_endorsement"] = new QuoteRule
        {
            Id = "tech_endorsement",
            Name = "Technology cyber endorsement",
            Conditions =
            [
                new RuleCondition
                {
                    QuestionKey = "industry",
                    Operator = RuleConditionOperator.Equals,
                    Value = "Technology"
                }
            ],
            Actions =
            [
                new RuleAction
                {
                    Type = RuleActionType.AddEndorsement,
                    Name = "Cyber Extension",
                }
            ]
        };
    }
}
