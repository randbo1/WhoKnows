using ServiceStack;

namespace MyApp.ServiceModel;

public enum ApplicationQuestionType
{
    Number,
    Date,
    Selection
}

public enum RuleConditionOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Before,
    After,
    In
}

public enum RuleActionType
{
    AddEndorsement,
    SetLimit,
    SetDeductible
}

public class QuestionDefinition
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public ApplicationQuestionType Type { get; set; }
    public List<string> Options { get; set; } = [];
}

public class RuleCondition
{
    public string QuestionKey { get; set; } = "";
    public RuleConditionOperator Operator { get; set; }
    public string Value { get; set; } = "";
}

public class RuleAction
{
    public RuleActionType Type { get; set; }
    public string Name { get; set; } = "";
    public decimal? NumericValue { get; set; }
}

public class QuoteRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RuleCondition> Conditions { get; set; } = [];
    public List<RuleAction> Actions { get; set; } = [];
}

public class ClientRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class ApplicationRecord
{
    public string Id { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public Dictionary<string, string> Answers { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}

public class QuoteResponse
{
    public List<string> Endorsements { get; set; } = [];
    public Dictionary<string, decimal> Limits { get; set; } = [];
    public Dictionary<string, decimal> Deductibles { get; set; } = [];
}

public class PolicyRecord
{
    public string Id { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public DateTime BoundAtUtc { get; set; }
    public QuoteResponse Quote { get; set; } = new();
}

public class BordereauRow
{
    public string PolicyNumber { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public DateTime BoundAtUtc { get; set; }
    public string Endorsements { get; set; } = "";
    public string Limits { get; set; } = "";
    public string Deductibles { get; set; } = "";
}

public class BordereauResponse
{
    public List<BordereauRow> Rows { get; set; } = [];
    public string Csv { get; set; } = "";
}

public class QuestionsResponse
{
    public List<QuestionDefinition> Items { get; set; } = [];
}

public class RulesResponse
{
    public List<QuoteRule> Items { get; set; } = [];
}

public class ClientsResponse
{
    public List<ClientRecord> Items { get; set; } = [];
    public ClientRecord? Created { get; set; }
}

public class ApplicationsResponse
{
    public List<ApplicationRecord> Items { get; set; } = [];
    public ApplicationRecord? Created { get; set; }
}

public class PoliciesResponse
{
    public List<PolicyRecord> Items { get; set; } = [];
}

[Route("/admin/questions", "GET POST")]
public class ManageQuestions : IReturn<QuestionsResponse>, IGet, IPost
{
    public QuestionDefinition? Question { get; set; }
}

[Route("/admin/rules", "GET POST")]
public class ManageRules : IReturn<RulesResponse>, IGet, IPost
{
    public QuoteRule? Rule { get; set; }
}

[Route("/clients", "GET POST")]
public class ManageClients : IReturn<ClientsResponse>, IGet, IPost
{
    public string? Name { get; set; }
}

[Route("/applications", "GET POST")]
public class ManageApplications : IReturn<ApplicationsResponse>, IGet, IPost
{
    public string? ClientId { get; set; }
    public string? ProductCode { get; set; }
    public Dictionary<string, string>? Answers { get; set; }
}

[Route("/applications/{ApplicationId}/quote", "POST")]
public class QuoteApplication : IPost, IReturn<QuoteResponse>
{
    public string ApplicationId { get; set; } = "";
}

[Route("/applications/{ApplicationId}/bind", "POST")]
public class BindPolicy : IPost, IReturn<PolicyRecord>
{
    public string ApplicationId { get; set; } = "";
}

[Route("/policies", "GET")]
public class ManagePolicies : IGet, IReturn<PoliciesResponse> {}

[Route("/reports/bordereau", "GET")]
public class GetBordereau : IGet, IReturn<BordereauResponse> {}
