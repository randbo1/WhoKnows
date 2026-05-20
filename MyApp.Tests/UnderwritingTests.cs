using MyApp.ServiceInterface;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.Testing;

namespace MyApp.Tests;

public class UnderwritingTests
{
    private readonly ServiceStackHost appHost;

    public UnderwritingTests()
    {
        appHost = new BasicAppHost().Init();
        appHost.Container.AddTransient<UnderwritingServices>();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => appHost.Dispose();

    [Test]
    public void Quote_rules_apply_to_endorsements_limits_and_deductibles()
    {
        var service = appHost.Container.Resolve<UnderwritingServices>();

        var createdClient = (ClientsResponse)service.Post(new ManageClients { Name = "Contoso" });
        var client = createdClient.Created!;

        var createdApp = (ApplicationsResponse)service.Post(new ManageApplications
        {
            ClientId = client.Id,
            ProductCode = "CommercialProperty",
            Answers = new Dictionary<string, string>
            {
                ["sum_insured"] = "150000",
                ["industry"] = "Technology",
                ["effective_date"] = "2026-07-01",
            }
        });

        var app = createdApp.Created!;
        var quote = (QuoteResponse)service.Post(new QuoteApplication { ApplicationId = app.Id });

        Assert.That(quote.Endorsements, Does.Contain("Cyber Extension"));
        Assert.That(quote.Limits["Property"], Is.EqualTo(100000));
        Assert.That(quote.Deductibles["Property"], Is.EqualTo(2500));
    }

    [Test]
    public void Binding_policy_is_included_in_bordereau_report()
    {
        var service = appHost.Container.Resolve<UnderwritingServices>();

        var createdClient = (ClientsResponse)service.Post(new ManageClients { Name = "Fabrikam" });
        var client = createdClient.Created!;

        var createdApp = (ApplicationsResponse)service.Post(new ManageApplications
        {
            ClientId = client.Id,
            ProductCode = "CommercialProperty",
            Answers = new Dictionary<string, string>
            {
                ["sum_insured"] = "200000",
                ["industry"] = "Retail",
                ["effective_date"] = "2026-08-01",
            }
        });

        var app = createdApp.Created!;
        var policy = (PolicyRecord)service.Post(new BindPolicy { ApplicationId = app.Id });
        var bordereau = (BordereauResponse)service.Get(new GetBordereau());

        Assert.That(policy.PolicyNumber, Does.StartWith("POL-"));
        Assert.That(bordereau.Rows.Any(x => x.PolicyNumber == policy.PolicyNumber), Is.True);
        Assert.That(bordereau.Rows.Any(x => x.ClientName == "Fabrikam"), Is.True);
        Assert.That(bordereau.Csv, Does.Contain("PolicyNumber"));
        Assert.That(bordereau.Csv, Does.Contain("Fabrikam"));
    }
}
