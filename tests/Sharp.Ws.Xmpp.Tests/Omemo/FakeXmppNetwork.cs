using Sharp.Xmpp;
using Sharp.Xmpp.Core;
using Sharp.Xmpp.Extensions;
using Sharp.Xmpp.Im;
using System.Collections.Concurrent;
using System.Xml;

namespace Sharp.Ws.Xmpp.Tests.Omemo;

/// <summary>
/// An in-process stand-in for an XMPP server: PEP storage per account, message routing
/// between clients, and sent carbons to the sender's other clients.
/// </summary>
internal sealed class FakeXmppNetwork
{
    private const string PubSub = "http://jabber.org/protocol/pubsub";

    private readonly object syncRoot = new();
    private readonly Dictionary<string, Dictionary<string, List<XmlElement>>> pep = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FakeClient> clients = new();
    private int pending;

    private readonly Dictionary<string, List<(FakeClient Client, string Nick)>> rooms = new(StringComparer.OrdinalIgnoreCase);

    public List<XmlElement> PublishOptions { get; } = new();

    /// <summary>
    /// Creates a non-anonymous room and sends every member the occupant presences with real JIDs.
    /// </summary>
    public void JoinRoom(string room, params (FakeClient Client, string Nick)[] members)
    {
        lock (syncRoot)
            rooms[room] = members.ToList();

        foreach (var receiver in members)
        {
            foreach (var occupant in members)
            {
                var presence = Parse($"<presence xmlns='jabber:client' from='{room}/{occupant.Nick}' to='{receiver.Client.Jid}'>" +
                    $"<x xmlns='http://jabber.org/protocol/muc#user'><item jid='{occupant.Client.Jid}' affiliation='member' role='participant'/></x></presence>");
                receiver.Client.Omemo.Input(new Sharp.Xmpp.Im.Presence(new Sharp.Xmpp.Core.Presence(presence)));
            }
        }
    }

    public FakeClient AddClient(string fullJid)
    {
        var client = new FakeClient(this, new Jid(fullJid));
        lock (syncRoot)
            clients.Add(client);
        return client;
    }

    public IReadOnlyList<XmlElement> GetItems(string bareJid, string node)
    {
        lock (syncRoot)
            return pep.TryGetValue(bareJid, out var nodes) && nodes.TryGetValue(node, out var items) ? items.ToList() : new List<XmlElement>();
    }

    /// <summary>Replaces a PEP node's items, e.g. to simulate a client that only speaks one OMEMO version.</summary>
    public void SetItems(string bareJid, string node, params string[] itemXml)
    {
        lock (syncRoot)
        {
            var list = Node(bareJid, node);
            list.Clear();
            list.AddRange(itemXml.Select(Parse));
        }
    }

    /// <summary>Waits until background work (empty replies, republishing) has settled.</summary>
    public void WaitIdle()
    {
        int quietRounds = 0;
        int last = -1;
        for (int i = 0; i < 100 && quietRounds < 5; i++)
        {
            Thread.Sleep(40);
            int now = Volatile.Read(ref pending);
            quietRounds = now == last ? quietRounds + 1 : 0;
            last = now;
        }
    }

    internal Iq HandleIq(FakeClient from, IqType type, Jid to, XmlElement data)
    {
        Interlocked.Increment(ref pending);
        string target = (to ?? from.Jid).GetBareJid().ToString();
        var copy = Clone(data);

        lock (syncRoot)
        {
            if (copy.LocalName == "pubsub" && copy.NamespaceURI == PubSub)
            {
                var publish = copy["publish", PubSub];
                if (publish != null)
                {
                    var options = copy["publish-options", PubSub];
                    if (options != null)
                        PublishOptions.Add(options);
                    var item = publish["item", PubSub]!;
                    var list = Node(target, publish.GetAttribute("node"));
                    list.RemoveAll(i => i.GetAttribute("id") == item.GetAttribute("id"));
                    list.Add(item);
                    return Result(from, null);
                }

                var retract = copy["retract", PubSub];
                if (retract != null)
                {
                    string id = retract["item", PubSub]!.GetAttribute("id");
                    Node(target, retract.GetAttribute("node")).RemoveAll(i => i.GetAttribute("id") == id);
                    return Result(from, null);
                }

                var itemsRequest = copy["items", PubSub];
                if (itemsRequest != null)
                {
                    string node = itemsRequest.GetAttribute("node");
                    string? itemId = itemsRequest["item", PubSub]?.GetAttribute("id");
                    var items = Node(target, node).Where(i => itemId == null || i.GetAttribute("id") == itemId).ToList();
                    if (items.Count == 0)
                        return ItemNotFound(from);

                    var response = Xml.Element("pubsub", PubSub);
                    var itemsElement = Xml.Element("items").Attr("node", node);
                    foreach (var i in items)
                        itemsElement.Child(i);
                    response.Child(itemsElement);
                    return Result(from, Parse(response.ToXmlString()));
                }
            }
        }

        return ItemNotFound(from);
    }

    internal void Route(FakeClient sender, Sharp.Xmpp.Im.Message message)
    {
        Interlocked.Increment(ref pending);
        message.From = sender.Jid;
        string wire = message.Data.ToXmlString();
        string toBare = message.To.GetBareJid().ToString();

        List<(FakeClient Client, string Nick)>? room;
        lock (syncRoot)
            rooms.TryGetValue(toBare, out room);
        if (room != null)
        {
            string nick = room.First(m => m.Client == sender).Nick;
            foreach (var member in room)
            {
                var copy = Parse(wire);
                copy.SetAttribute("from", toBare + "/" + nick);
                copy.SetAttribute("to", member.Client.Jid.ToString());
                member.Client.Deliver(copy);
            }
            return;
        }

        List<FakeClient> targets;
        List<FakeClient> carbons;
        lock (syncRoot)
        {
            targets = clients.Where(c => c.Jid.GetBareJid().ToString() == toBare).ToList();
            carbons = clients.Where(c => c != sender && c.Jid.GetBareJid().ToString() == sender.Jid.GetBareJid().ToString() && !targets.Contains(c)).ToList();
        }

        foreach (var c in targets)
            c.Deliver(Parse(wire));

        bool isPrivate = message.Data["private", "urn:xmpp:carbons:2"] != null;
        foreach (var c in isPrivate ? new List<FakeClient>() : carbons)
        {
            var carbon = Parse($"<message xmlns='jabber:client' from='{sender.Jid.GetBareJid()}' to='{c.Jid}'>" +
                $"<sent xmlns='urn:xmpp:carbons:2'><forwarded xmlns='urn:xmpp:forward:0'>{wire}</forwarded></sent></message>");
            c.Deliver(carbon);
        }
    }

    private List<XmlElement> Node(string bare, string node)
    {
        if (!pep.TryGetValue(bare, out var nodes))
            pep[bare] = nodes = new Dictionary<string, List<XmlElement>>();
        if (!nodes.TryGetValue(node, out var items))
            nodes[node] = items = new List<XmlElement>();
        return items;
    }

    private static Iq Result(FakeClient to, XmlElement? data) => new(IqType.Result, "1", to.Jid, null, data);

    private static Iq ItemNotFound(FakeClient to)
    {
        var error = Parse("<error type='cancel' xmlns='jabber:client'><item-not-found xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error>");
        var iq = new Iq(IqType.Error, "1", to.Jid, null, null);
        iq.Data.Child(error);
        return iq;
    }

    internal static XmlElement Parse(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc.DocumentElement!;
    }

    private static XmlElement Clone(XmlElement e) => Parse(e.ToXmlString());

    internal void Settled() => Interlocked.Increment(ref pending);
}

internal sealed class FakeClient : IOmemoTransport
{
    private readonly FakeXmppNetwork network;

    public FakeClient(FakeXmppNetwork network, Jid jid)
    {
        this.network = network;
        Jid = jid;
        Omemo = new OmemoEncryption(new XmppIm("localhost", "user", "password"));
        Omemo.UseForTesting(this, Store);
    }

    public Jid Jid { get; }

    public InMemoryOmemoStore Store { get; } = new();

    public OmemoEncryption Omemo { get; }

    /// <summary>Messages the application would see (not swallowed by the OMEMO filter).</summary>
    public ConcurrentQueue<Sharp.Xmpp.Im.Message> Inbox { get; } = new();

    /// <summary>Everything this client sent, as serialized on the wire.</summary>
    public ConcurrentQueue<string> Sent { get; } = new();

    public Iq IqRequest(IqType type, Jid to, XmlElement data, int millisecondsTimeout) => network.HandleIq(this, type, to, data);

    public void SendMessage(Sharp.Xmpp.Im.Message message)
    {
        Omemo.Output(message);
        Sent.Enqueue(message.Data.OuterXml);
        network.Route(this, message);
    }

    internal void Deliver(XmlElement element)
    {
        var message = new Sharp.Xmpp.Im.Message(new Sharp.Xmpp.Core.Message(element));
        if (Omemo.Input(message))
            return;

        var sent = message.Data["sent", "urn:xmpp:carbons:2"];
        if (sent != null)
            message = new Sharp.Xmpp.Im.Message(new Sharp.Xmpp.Core.Message(sent["forwarded", "urn:xmpp:forward:0"]!["message"]!));
        if (message.Data["body"] != null)
            Inbox.Enqueue(message);
        network.Settled();
    }

    public void Send(string to, string body) =>
        SendMessage(new Sharp.Xmpp.Im.Message(new Jid(to), body, type: MessageType.Chat));
}
