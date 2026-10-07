"""Builds one plain-language OTIS role guide PDF per role."""
import os
import sys

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import (
    BaseDocTemplate, Frame, KeepTogether, PageTemplate, Paragraph, Spacer, Table, TableStyle,
)

OUT = sys.argv[1]

TEAL_DARK = colors.HexColor("#0a3535")
TEAL = colors.HexColor("#2b5257")
TEAL_LIGHT = colors.HexColor("#ccfdf3")
GREEN_HI = colors.HexColor("#b0ed70")
GREY_BG = colors.HexColor("#f1f4f4")
GREY_LINE = colors.HexColor("#cfd8d8")
INK = colors.HexColor("#1d2b2b")
MUTED = colors.HexColor("#5b6b6b")

PAGE_W, PAGE_H = A4


def P(text, style, **kw):
    return Paragraph(text.replace(' - ', ' – '), style, **kw)

MARGIN = 18 * mm
CONTENT_W = PAGE_W - 2 * MARGIN

S = {
    "body": ParagraphStyle("body", fontName="Helvetica", fontSize=10, leading=14.5, textColor=INK),
    "lead": ParagraphStyle("lead", fontName="Helvetica", fontSize=11.5, leading=16.5, textColor=INK),
    "h2": ParagraphStyle("h2", fontName="Helvetica-Bold", fontSize=13, leading=17, textColor=TEAL_DARK,
                         spaceBefore=12, spaceAfter=6, keepWithNext=1),
    "bullet": ParagraphStyle("bullet", fontName="Helvetica", fontSize=10, leading=14.5, textColor=INK,
                             leftIndent=14, bulletIndent=2, spaceAfter=3),
    "step": ParagraphStyle("step", fontName="Helvetica", fontSize=10, leading=14.5, textColor=INK),
    "stepnum": ParagraphStyle("stepnum", fontName="Helvetica-Bold", fontSize=10.5, leading=14.5,
                              textColor=colors.white, alignment=1),
    "cell": ParagraphStyle("cell", fontName="Helvetica", fontSize=9.5, leading=13.5, textColor=INK),
    "cellhead": ParagraphStyle("cellhead", fontName="Helvetica-Bold", fontSize=10, leading=13.5,
                               textColor=TEAL_DARK),
    "callout": ParagraphStyle("callout", fontName="Helvetica", fontSize=10, leading=14.5, textColor=INK),
    "journey": ParagraphStyle("journey", fontName="Helvetica", fontSize=8, leading=10, textColor=MUTED,
                              alignment=1),
    "journey_on": ParagraphStyle("journey_on", fontName="Helvetica-Bold", fontSize=8, leading=10,
                                 textColor=TEAL_DARK, alignment=1),
    "caption": ParagraphStyle("caption", fontName="Helvetica-Oblique", fontSize=8.5, leading=11.5,
                              textColor=MUTED, alignment=TA_LEFT),
}

JOURNEY = ["Case received", "Tax check<br/>(if needed)", "AQS check", "Adviser puts<br/>things right",
           "T&amp;C Manager<br/>signs off", "Case closed"]


# ---------- building blocks ----------

def h2(text):
    return P(text, S["h2"])


def para(text, style="body"):
    return P(text, S[style])


def bullets(items):
    return [P(i, S["bullet"], bulletText="•") for i in items]


def callout(title, text):
    content = [P(f"<b>{title}</b>", S["callout"]), Spacer(1, 2), P(text, S["callout"])]
    t = Table([[content]], colWidths=[CONTENT_W])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), TEAL_LIGHT),
        ("LINEBEFORE", (0, 0), (0, -1), 3, TEAL),
        ("LEFTPADDING", (0, 0), (-1, -1), 10), ("RIGHTPADDING", (0, 0), (-1, -1), 10),
        ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
    ]))
    return KeepTogether([Spacer(1, 4), t, Spacer(1, 4)])


def steps(items):
    rows = []
    for n, text in enumerate(items, 1):
        rows.append([P(str(n), S["stepnum"]), P(text, S["step"])])
    t = Table(rows, colWidths=[9 * mm, CONTENT_W - 9 * mm])
    style = [
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (1, 0), (1, -1), 8), ("LEFTPADDING", (0, 0), (0, -1), 0),
        ("RIGHTPADDING", (0, 0), (0, -1), 0),
        ("TOPPADDING", (0, 0), (-1, -1), 3), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]
    for r in range(len(rows)):
        style.append(("BACKGROUND", (0, r), (0, r), TEAL))
    t.setStyle(TableStyle(style))
    return t


def two_col(left_title, left_items, right_title, right_items):
    def cell(title, items):
        out = [P(title, S["cellhead"]), Spacer(1, 4)]
        out += [P(i, S["bullet"], bulletText="•") for i in items]
        return out
    half = CONTENT_W / 2
    t = Table([[cell(left_title, left_items), cell(right_title, right_items)]], colWidths=[half - 3, half - 3],
              spaceBefore=2)
    t.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("BACKGROUND", (0, 0), (0, 0), GREY_BG), ("BACKGROUND", (1, 0), (1, 0), GREY_BG),
        ("LINEABOVE", (0, 0), (0, 0), 3, GREEN_HI), ("LINEABOVE", (1, 0), (1, 0), 3, GREY_LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 9), ("RIGHTPADDING", (0, 0), (-1, -1), 9),
        ("TOPPADDING", (0, 0), (-1, -1), 8), ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
    ]))
    return KeepTogether([t])


def grid(header, rows, widths):
    data = [[P(h, S["cellhead"]) for h in header]]
    data += [[P(c, S["cell"]) for c in r] for r in rows]
    t = Table(data, colWidths=[w * CONTENT_W for w in widths], repeatRows=1)
    style = [
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LINEBELOW", (0, 0), (-1, 0), 1.2, TEAL),
        ("LINEBELOW", (0, 1), (-1, -1), 0.4, GREY_LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]
    for r in range(1, len(data)):
        if r % 2 == 0:
            style.append(("BACKGROUND", (0, r), (-1, r), GREY_BG))
    t.setStyle(TableStyle(style))
    return t


def journey(highlight, caption):
    box_w, arrow_w = 25 * mm, (CONTENT_W - 6 * 25 * mm) / 5
    cells, widths = [], []
    for i, label in enumerate(JOURNEY):
        on = i in highlight
        cells.append(P(label, S["journey_on" if on else "journey"]))
        widths.append(box_w)
        if i < len(JOURNEY) - 1:
            cells.append(P("<font color='#2b5257'>›</font>", S["journey_on"]))
            widths.append(arrow_w)
    t = Table([cells], colWidths=widths, rowHeights=[12 * mm])
    style = [("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
             ("LEFTPADDING", (0, 0), (-1, -1), 2), ("RIGHTPADDING", (0, 0), (-1, -1), 2)]
    for i in range(len(JOURNEY)):
        col = i * 2
        if i in highlight:
            style += [("BACKGROUND", (col, 0), (col, 0), GREEN_HI), ("BOX", (col, 0), (col, 0), 1, TEAL)]
        else:
            style += [("BACKGROUND", (col, 0), (col, 0), GREY_BG), ("BOX", (col, 0), (col, 0), 0.5, GREY_LINE)]
    t.setStyle(TableStyle(style))
    return KeepTogether([t, Spacer(1, 4), P(caption, S["caption"])])


# ---------- page furniture ----------

def make_doc(path, role, tagline):
    doc = BaseDocTemplate(path, pagesize=A4, leftMargin=MARGIN, rightMargin=MARGIN,
                          topMargin=MARGIN, bottomMargin=MARGIN,
                          title=f"OTIS user guide - {role}", author="OTIS",
                          subject=f"What the {role} role does in OTIS")
    first_top = 62 * mm
    later_top = 22 * mm

    def first(c, d):
        c.saveState()
        c.setFillColor(TEAL_DARK)
        c.rect(0, PAGE_H - 50 * mm, PAGE_W, 50 * mm, stroke=0, fill=1)
        c.setFillColor(GREEN_HI)
        c.rect(0, PAGE_H - 51.5 * mm, PAGE_W, 1.5 * mm, stroke=0, fill=1)
        c.setFillColor(GREEN_HI)
        c.setFont("Helvetica-Bold", 9.5)
        c.drawString(MARGIN, PAGE_H - 16 * mm, "OTIS  ·  USER GUIDE")
        c.setFillColor(colors.white)
        c.setFont("Helvetica-Bold", 26)
        c.drawString(MARGIN, PAGE_H - 30 * mm, role)
        c.setFont("Helvetica", 11.5)
        c.setFillColor(colors.HexColor("#d9efea"))
        c.drawString(MARGIN, PAGE_H - 39 * mm, tagline)
        footer(c, d)
        c.restoreState()

    def later(c, d):
        c.saveState()
        c.setFillColor(TEAL_DARK)
        c.rect(0, PAGE_H - 12 * mm, PAGE_W, 12 * mm, stroke=0, fill=1)
        c.setFillColor(colors.white)
        c.setFont("Helvetica-Bold", 9)
        c.drawString(MARGIN, PAGE_H - 7.6 * mm, f"OTIS user guide  ·  {role}")
        footer(c, d)
        c.restoreState()

    def footer(c, d):
        c.setStrokeColor(GREY_LINE)
        c.setLineWidth(0.5)
        c.line(MARGIN, 12 * mm, PAGE_W - MARGIN, 12 * mm)
        c.setFont("Helvetica", 8)
        c.setFillColor(MUTED)
        c.drawString(MARGIN, 8 * mm, "OTIS user guide  ·  October 2026")
        c.drawRightString(PAGE_W - MARGIN, 8 * mm, f"{role}  ·  Page {d.page}")

    f1 = Frame(MARGIN, 16 * mm, CONTENT_W, PAGE_H - first_top - 16 * mm, id="f1", leftPadding=0,
               rightPadding=0, topPadding=0, bottomPadding=0)
    f2 = Frame(MARGIN, 16 * mm, CONTENT_W, PAGE_H - later_top - 16 * mm, id="f2", leftPadding=0,
               rightPadding=0, topPadding=0, bottomPadding=0)
    doc.addPageTemplates([PageTemplate(id="first", frames=[f1], onPage=first, autoNextPageTemplate="later"),
                          PageTemplate(id="later", frames=[f2], onPage=later)])
    return doc


HELP_DEFAULT = ("If something doesn't look right - a case you expected is missing, you can't open a page, "
                "or an action is refused - speak to your team manager first. They can raise it with the "
                "OTIS administrator. When you get in touch, include the case reference and a short "
                "description of what you were trying to do.")


# ---------- content ----------

GUIDES = []

# AQS Checker
GUIDES.append(dict(
    file="OTIS Guide - AQS Checker.pdf",
    role="AQS Checker",
    tagline="Checking the quality of advice files and recording the outcome",
    story=lambda: [
        para("As an <b>AQS Checker</b> you check the quality of advice. Using the standard checklist, you "
             "review the <b>Advice Quality</b> and <b>File Quality</b> of each case, record what you find and "
             "give the case its outcome. Where something falls short, you set out what the adviser needs to do "
             "to put it right. Your work is what makes the firm's advice reviews consistent, fair and on the "
             "record.", "lead"),
        callout("Where you'll work",
                "In the <b>OTIS portal</b>, on the <b>AQS reviews</b> page. Sign in with your usual work account."),
        h2("Where you fit in"),
        journey({2}, "Your part of the journey is the AQS check. Some cases have a Tax check before yours."),
        h2("What you can do"),
        *bullets([
            "See the queue of cases waiting for an AQS check, and <b>pick one up yourself</b>.",
            "Work on cases your <b>AQS Team Manager</b> has allocated to you.",
            "Open the case details you need, including any Tax check already completed on the case.",
            "Answer the checklist section by section. Your answers are saved as you go.",
            "Record fail reasons, comments and File Quality fail points.",
            "Record <b>who carries each fail</b> - the adviser, the paraplanner or someone else.",
            "Write a <b>remedial action</b> for every fail point.",
            "Give the case its outcome, submit the check and save it as a PDF.",
        ]),
        h2("How a typical case works for you"),
        steps([
            "Open the <b>AQS reviews</b> page. Cases waiting for a checker appear in two groups: "
            "<b>Tax review completed - awaiting allocation</b> first, then <b>New - awaiting allocation</b>. "
            "Each group is ordered oldest first.",
            "Pick up a case. It becomes yours and moves to <b>Allocated to me</b>. Your manager may also "
            "allocate cases to you directly.",
            "Work through the checklist. You can stop and come back at any time; the case then shows "
            "under <b>In progress</b>.",
            "Where an answer falls short, record the reason and your comments, tick any File Quality fail "
            "points that apply, and say who carries the fail.",
            "Choose the outcome: <b>Pass</b>, <b>Pass with issues</b>, <b>Insufficient evidence</b> or "
            "<b>Potential harm</b>.",
            "Under <b>Fail points and remedial actions</b>, write what needs to be done to put each point right. "
            "OTIS won't let you submit until every fail point has one.",
            "Select <b>Submit this review</b>. The check is then locked and moves to <b>Completed</b>.",
        ]),
        callout("What happens after you submit",
                "If the case passes, it closes - nobody else needs to act. If it doesn't, the case goes to the "
                "adviser to put right, and their T&amp;C Manager signs it off. The paraplanner is emailed a "
                "copy of your completed check."),
        h2("What you'll see - and what you won't"),
        two_col("You can see", [
            "Cases waiting for an AQS check (a summary only, until you pick one up).",
            "Cases allocated to you.",
            "Checks you have completed.",
        ], "You won't see", [
            "Cases allocated to other checkers.",
            "The detail of a waiting case before you take it.",
            "An option to change a check after it has been submitted.",
        ]),
        h2("Emails you'll receive"),
        *bullets(["When your AQS Team Manager allocates a case to you, you'll get an email with a link to it."]),
        h2("Good to know"),
        *bullets([
            "Cases that have already had a Tax check are flagged <b>Tax reviewed</b> and listed first. They have "
            "usually been in OTIS longer, so please prioritise them.",
            "Each waiting case shows <b>Days in OTIS</b> and <b>Waiting for AQS</b>, counted in working days, "
            "plus its due date.",
            "Only pick up what you have capacity to finish. Once you take a case, other checkers can no "
            "longer see it.",
            "If you took a case by mistake or can't complete it, ask your AQS Team Manager to reallocate it. "
            "Your work so far is kept.",
        ]),
        h2("Need help?"),
        para(HELP_DEFAULT),
    ],
))

# Tax Checker
GUIDES.append(dict(
    file="OTIS Guide - Tax Checker.pdf",
    role="Tax Checker",
    tagline="Carrying out the Tax review on cases that need one",
    story=lambda: [
        para("As a <b>Tax Checker</b> you carry out the <b>Tax review</b> on cases that need one. You record "
             "the Tax grade and your evidence, set out what needs putting right, and decide whether the case "
             "goes on for an AQS check. Your <b>Tax Team Manager</b> decides which cases come to you.", "lead"),
        callout("Where you'll work",
                "In the <b>OTIS portal</b>, on the <b>Tax reviews</b> page. Sign in with your usual work account."),
        h2("Where you fit in"),
        journey({1}, "Your part of the journey is the Tax check. Most cases you complete then go on to an AQS check."),
        h2("What you can do"),
        *bullets([
            "See the cases your Tax Team Manager has <b>allocated to you</b>.",
            "Open the client and case information you need for the Tax review.",
            "Complete the Tax checklist. Your answers are saved as you go.",
            "Record fail reasons, comments and <b>who carries each fail</b>.",
            "Write a <b>remedial action</b> for every fail point.",
            "Choose what happens next: send the case on to AQS, or return it to the paraplanner.",
            "Submit the Tax review and save it as a PDF.",
        ]),
        h2("How a typical case works for you"),
        steps([
            "Your Tax Team Manager allocates a case to you and you receive an email.",
            "Open the <b>Tax reviews</b> page. Your cases are grouped as <b>Allocated</b>, <b>In progress</b> "
            "and <b>Completed</b>.",
            "Open the case and work through the Tax checklist. You can stop and come back at any time.",
            "Where something falls short, record the reason and your comments, and say who carries the fail.",
            "Write a remedial action for each fail point. OTIS won't let you submit until every one has one.",
            "Under <b>For Tax team usage</b>, choose <b>Submit to AQS</b> (the case moves on for an AQS check) "
            "or <b>Return to paraplanner</b> (the case ends with your Tax check and no AQS check follows).",
            "Select <b>Submit this review</b>. The review is locked and moves to <b>Completed</b>.",
        ]),
        callout("What happens after you submit",
                "If you chose <b>Submit to AQS</b>, the case goes straight into the AQS queue, flagged as "
                "<b>Tax reviewed</b> and placed at the front. You don't need to pass it on yourself. If your "
                "check found issues, the adviser is only asked to put them right once every check on the "
                "case is finished."),
        h2("What you'll see - and what you won't"),
        two_col("You can see", [
            "Cases allocated to you.",
            "Your Tax reviews in progress.",
            "Tax reviews you have completed.",
        ], "You won't see", [
            "Tax cases that haven't been allocated yet.",
            "Cases allocated to other Tax Checkers.",
            "Options to allocate or reallocate cases - that's your manager's job.",
            "An option to change a review after it has been submitted.",
        ]),
        h2("Emails you'll receive"),
        *bullets(["When a case is allocated to you, you'll get an email with a link to it."]),
        h2("Good to know"),
        *bullets([
            "If a case has reached you that you don't think needs a Tax check, speak to your Tax Team Manager "
            "rather than submitting it.",
            "A submitted Tax review can't be edited, because the next step has already started from it.",
            "If a case is moved to another Tax Checker, the work done so far is kept.",
        ]),
        h2("Need help?"),
        para(HELP_DEFAULT),
    ],
))

# Paraplanner
GUIDES.append(dict(
    file="OTIS Guide - Paraplanner.pdf",
    role="Paraplanner",
    tagline="Staying informed about checks on the cases you worked on",
    story=lambda: [
        para("As a <b>Paraplanner</b> you support advisers in preparing advice and the client file. "
             "<b>You don't need to sign in to OTIS.</b> Instead, you're kept informed by email whenever a check "
             "on one of your cases is completed, with a copy of the findings attached.", "lead"),
        callout("Where you'll work",
                "In your <b>email inbox</b>. Paraplanners don't have an OTIS sign-in, and there are no forms "
                "for you to complete in OTIS."),
        h2("Where you fit in"),
        journey({1, 2}, "You receive an email each time a Tax or AQS check on one of your cases is submitted."),
        h2("What's expected of you"),
        *bullets([
            "Read the findings when a check on one of your cases is completed.",
            "Look out for fail points marked as yours - a checker can record that a fail is carried by the "
            "paraplanner rather than the adviser.",
            "Work with the adviser to put right anything that falls to you.",
            "Keep client files complete and up to date in <b>Intelligent Office</b>. Checks are carried out "
            "against what is on file.",
            "Make sure your <b>work email address</b> is recorded correctly on cases in Intelligent Office. "
            "That's how OTIS knows where to send your emails.",
        ]),
        h2("How it works"),
        steps([
            "A case you worked on is selected for checking and brought into OTIS from Intelligent Office.",
            "A Tax Checker and/or an AQS Checker reviews the file.",
            "When each check is submitted, you receive an email.",
            "The email carries a <b>PDF of each completed check</b> - every answer, grouped by section, with the "
            "outcome and any fail points.",
            "Where remedial actions on the case have been completed, the email also includes the "
            "<b>Remediation and escalation form</b>.",
            "The adviser records the remediation in OTIS and their T&amp;C Manager signs it off.",
        ]),
        h2("Emails you'll receive"),
        grid(["When", "What's attached"], [
            ["A Tax or AQS check on one of your cases is submitted",
             "A PDF of each completed check on the case (Tax and/or AQS). Where remedial actions on the "
             "case have already been completed, the Remediation and escalation form is attached too."],
        ], [0.45, 0.55]),
        h2("Good to know"),
        *bullets([
            "If the Tax team chooses <b>Return to paraplanner</b>, the case ends at the Tax check and does not "
            "go on to AQS. The Tax check PDF you receive shows what they found.",
            "The attachments contain client information. Store and share them only in line with the firm's "
            "data handling rules.",
            "If you stop receiving emails you'd expect, check that your email address is recorded on the case "
            "in Intelligent Office.",
        ]),
        h2("Need help?"),
        para("If an email or attachment doesn't look right, speak to the adviser on the case or your team "
             "manager. They can raise it with the OTIS administrator. Please include the case reference."),
    ],
))

# Adviser
GUIDES.append(dict(
    file="OTIS Guide - Adviser.pdf",
    role="Adviser",
    tagline="Seeing your check outcomes and putting things right",
    story=lambda: [
        para("As an <b>Adviser</b>, OTIS is where you see the outcome of checks on your own advice files and "
             "record what you've done to put right anything that was found. You only see a case once "
             "<b>every check on it is finished</b> and there is something for you to act on.", "lead"),
        callout("Where you'll work",
                "In the <b>OTIS portal</b>, on the <b>My Work</b> and <b>Remediation</b> pages. Sign in with your "
                "usual work account."),
        h2("Where you fit in"),
        journey({3}, "Your part of the journey starts once the checks are complete and remedial action is needed."),
        h2("What you can do"),
        *bullets([
            "See your own cases that need <b>remedial action</b>.",
            "Read each fail point, what the checker found and the remedial action they recommend.",
            "Record for each action whether it was <b>performed</b>, with an optional note.",
            "Answer whether <b>client contact</b> is required.",
            "Save a draft and come back to it later.",
            "Send the remediation to your <b>T&amp;C Manager</b> for sign-off, where the grade needs it.",
            "Follow your cases through sign-off to closure, and save the form as a PDF.",
        ]),
        h2("How a typical case works for you"),
        steps([
            "You receive an email when one of your cases needs remedial action.",
            "Open <b>My Work</b> or <b>Remediation</b>. Your cases are grouped as <b>Remedial action required</b>, "
            "<b>Awaiting T&amp;C sign-off</b> and <b>Closed</b>.",
            "Open the case. The <b>Remediation and escalation</b> form lists each fail point with the checker's "
            "remedial action and its target date.",
            "For each action, choose <b>Action performed: Yes</b> or <b>No</b>, and add a note if it helps.",
            "Answer <b>Client contact required?</b> - Yes, No or Potentially.",
            "Select <b>Save draft</b> to keep your progress, or <b>Save and sign off this remediation</b> when "
            "you've finished. A <b>Pass with issues</b> case closes there. A case graded <b>Insufficient "
            "evidence</b> or <b>Potential harm</b>, or that failed its Tax check, moves to <b>Awaiting T&amp;C "
            "sign-off</b>.",
            "Your T&amp;C Manager reviews it. If they approve, the case closes. If they reject it, it comes back "
            "to you with notes explaining what to correct.",
        ]),
        h2("What you'll see - and what you won't"),
        two_col("You can see", [
            "Your own cases that need remedial action.",
            "Your cases waiting for T&amp;C sign-off.",
            "Your closed cases.",
            "The full case record and completed checks for those cases.",
        ], "You won't see", [
            "Cases belonging to other advisers, or their clients.",
            "Cases still waiting for, or going through, a Tax or AQS check.",
            "Draft findings that haven't been formally issued.",
            "Cases that passed - there is nothing for you to do on those.",
        ]),
        h2("Emails you'll receive"),
        *bullets([
            "When a case needs remedial action from you.",
            "When your T&amp;C Manager rejects a remediation and sends it back, with their notes.",
            "When a case is completed.",
        ]),
        h2("Good to know"),
        *bullets([
            "You can answer <b>No</b> to Action performed and still send the form. Explain why in the note - "
            "your T&amp;C Manager decides whether that is acceptable.",
            "Keep an eye on target dates. An action that has passed its target date is highlighted.",
            "Use <b>Open the full case record</b> to see the case details and the completed checks.",
            "If a case appears that isn't yours, or one you expected hasn't appeared, let your T&amp;C Manager "
            "know.",
        ]),
        h2("Need help?"),
        para(HELP_DEFAULT.replace("speak to your team manager first", "speak to your T&amp;C Manager first")),
    ],
))

# T&C Manager
GUIDES.append(dict(
    file="OTIS Guide - T&C Manager.pdf",
    role="T&C Manager",
    tagline="Signing off remediation and recording the final outcome",
    story=lambda: [
        para("As a <b>T&amp;C Manager</b> you check and approve the remediation completed by the advisers you "
             "supervise. You decide whether each remediation is good enough, send it back if it isn't, and "
             "record the case's <b>final outcome</b>.", "lead"),
        callout("Where you'll work",
                "In the <b>OTIS portal</b>, on the <b>Remediation</b> page. Sign in with your usual work account."),
        h2("Where you fit in"),
        journey({4}, "Your part of the journey is sign-off, after the adviser has recorded their remediation."),
        h2("What you can do"),
        *bullets([
            "See remediation cases for the <b>advisers mapped to you</b>, once all checks on them are finished.",
            "Review each fail point, the checker's remedial action and the adviser's response.",
            "<b>Approve</b> or <b>reject</b> the remediation, with notes.",
            "Answer <b>Recheck required?</b> and <b>Do the remedial actions change the advice?</b>",
            "Record the final outcome: <b>Pass</b>, <b>Pass with issues</b>, <b>Insufficient evidence</b> or "
            "<b>Potential harm</b>.",
            "Save the remediation form as a PDF.",
        ]),
        h2("How a typical sign-off works"),
        steps([
            "You receive an email when one of your advisers sends a remediation for sign-off.",
            "Open the case from the <b>Remediation</b> page. It shows under <b>Awaiting T&amp;C sign-off</b>.",
            "Read each remedial action, the adviser's <b>Action performed</b> answer and any note.",
            "Under <b>All remedial actions checked and approved?</b> choose <b>Approved</b> or <b>Rejected</b>.",
            "If you reject it, explain in <b>Notes</b> what must be corrected. The case goes back to the adviser.",
            "If you approve it, answer the two questions and choose the <b>Final outcome</b> - or choose "
            "<b>Leave for a separate regrade</b> if you need more time.",
            "If you left it for a separate regrade, record the final outcome later in the <b>Regraded outcome</b> "
            "panel and give a reason. This closes the case.",
        ]),
        callout("Choosing the final outcome",
                "For a case graded <b>Insufficient evidence</b>: if the missing evidence has now been provided, you "
                "can regrade it to <b>Pass</b>. If it can't be found and the matter warrants it, regrade it to "
                "<b>Potential harm</b>. The original outcome is always kept alongside the final one, and your "
                "reason is recorded in the case history."),
        h2("What you'll see - and what you won't"),
        two_col("You can see", [
            "Remediation for the advisers you supervise.",
            "Their cases waiting for your sign-off.",
            "Their closed cases.",
        ], "You won't see", [
            "Cases for advisers who aren't mapped to you.",
            "Cases still waiting for, or going through, a Tax or AQS check.",
            "Case allocation - that's handled by the Tax and AQS Team Managers.",
        ]),
        h2("Emails you'll receive"),
        *bullets(["When an adviser you supervise sends a remediation for your sign-off."]),
        h2("Good to know"),
        *bullets([
            "Which advisers you supervise is set by the OTIS administrator. If one of your advisers is missing, "
            "ask the administrator to update the <b>adviser mapping</b> - their cases can't be signed off until "
            "it's in place.",
            "An adviser can answer <b>No</b> to Action performed and still send the form. It's your call whether "
            "their explanation is acceptable.",
            "When rejecting, be specific. Your notes are what the adviser works from.",
        ]),
        h2("Need help?"),
        para("If something doesn't look right - a case is missing, or an action is refused - contact the OTIS "
             "administrator with the case reference and a short description of what you were trying to do."),
    ],
))

# Administrator
GUIDES.append(dict(
    file="OTIS Guide - Administrator.pdf",
    role="Administrator",
    tagline="Looking after OTIS: cases, people, settings and reporting",
    story=lambda: [
        para("As an <b>Administrator</b> you look after OTIS as a whole. You bring cases in, keep people and "
             "their roles up to date, maintain the checklist and settings, oversee the work across both review "
             "teams, and produce reports and the Trail Light export.", "lead"),
        callout("Where you'll work",
                "Mainly in the <b>OTIS management app</b>. You can also see every case in the <b>OTIS portal</b>, "
                "which is where checkers, advisers and T&amp;C Managers do their work."),
        h2("Where you fit in"),
        journey({0, 1, 2, 3, 4, 5}, "You oversee the whole journey, from bringing cases in to exporting closed results."),
        h2("What you can do"),
        para("The management app is organised into the areas below. Each one is a page in the menu."),
        Spacer(1, 4),
        grid(["Area", "What you do there"], [
            ["<b>Dashboard</b>", "See work in progress and outcomes at a glance."],
            ["<b>Case worklist</b>", "See every case, update case details and status, and allocate or reallocate "
                                     "Tax and AQS checks. Work already done is kept when a case is reallocated."],
            ["<b>Case intake</b>", "Upload the Intelligent Office extract, see how many rows came in, and deal "
                                   "with any rows that failed the checks."],
            ["<b>Management reporting</b>", "Report on outcomes, open and overdue remediation, ageing and "
                                            "sign-off. Filter the results and export them."],
            ["<b>Exports</b>", "Produce the Trail Light file of closed cases, when you need it."],
            ["<b>Question library</b>", "Maintain checklist sections and questions. Questions are retired by "
                                        "date, never deleted, so past checks always read as they did."],
            ["<b>Adviser mapping</b>", "Link each adviser to their T&amp;C Manager. Sign-off depends on it."],
            ["<b>Notification wording</b>", "Edit the wording of the emails OTIS sends."],
            ["<b>Dropdown options</b>", "Maintain the choices offered in lists across OTIS."],
            ["<b>Security configuration</b>", "Control which roles can reach which pages and actions."],
            ["<b>People</b>", "Grant and withdraw roles. One person can hold more than one role."],
        ], [0.28, 0.72]),
        h2("Your regular tasks"),
        grid(["How often", "What to do"], [
            ["Daily", "Upload the Intelligent Office extract and resolve any exceptions. Check for cases waiting "
                      "too long for allocation and for overdue remediation."],
            ["When people join, move or leave", "Update their roles on the <b>People</b> page and keep the "
                                                 "<b>Adviser mapping</b> current."],
            ["As needed", "Reallocate cases, correct case details, produce reports and the Trail Light export."],
            ["When the checklist changes", "Date questions or sections in and out in the <b>Question library</b>."],
        ], [0.28, 0.72]),
        h2("The roles you manage"),
        grid(["Role", "What they do", "Where"], [
            ["AQS Checker", "Picks up and completes AQS checks.", "Portal"],
            ["Tax Checker", "Completes Tax checks allocated to them.", "Portal"],
            ["AQS Team Manager", "Allocates AQS checks and watches the team's workload.", "App"],
            ["Tax Team Manager", "Allocates Tax checks and watches the team's workload.", "App"],
            ["Adviser", "Records remediation on their own cases.", "Portal"],
            ["T&amp;C Manager", "Signs off remediation and records the final outcome.", "Portal"],
            ["Paraplanner", "Receives emails with the completed checks. No sign-in.", "Email"],
            ["OTIS Manager", "Oversees all cases and can allocate either type of check.", "App"],
            ["Administrator", "Everything on this page.", "App and portal"],
        ], [0.27, 0.53, 0.20]),
        h2("Good to know"),
        *bullets([
            "Important changes - role grants, reallocations, status changes and sign-off decisions - are "
            "recorded with who made them, when and why.",
            "A closed case can't be edited. Correcting an outcome is a controlled step that needs a reason, and "
            "the original outcome is always kept.",
            "A role decides what a person can see. Grant only what someone needs for their job.",
            "Tax and AQS Team Managers can only allocate checks for their own team, and only to people who "
            "hold the matching checker role.",
            "Check the Trail Light file before it is sent on.",
        ]),
        h2("Need help?"),
        para("For anything you can't resolve from the management app, contact the OTIS system owner with the "
             "case reference or page name, and a short description of what you were trying to do."),
    ],
))


def main():
    os.makedirs(OUT, exist_ok=True)
    for g in GUIDES:
        path = os.path.join(OUT, g["file"])
        doc = make_doc(path, g["role"], g["tagline"])
        doc.build(g["story"]())
        print(path)


if __name__ == "__main__":
    main()
