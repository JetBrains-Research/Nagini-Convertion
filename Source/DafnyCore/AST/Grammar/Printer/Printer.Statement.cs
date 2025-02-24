//-----------------------------------------------------------------------------
//
// Copyright (C) Microsoft Corporation.  All Rights Reserved.
// Copyright by the contributors to the Dafny Project
// SPDX-License-Identifier: MIT
//
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics.Contracts;
using System.Numerics;
using System.Linq;
using DafnyCore;
using JetBrains.Annotations;
using Bpl = Microsoft.Boogie;

namespace Microsoft.Dafny {

  public partial class Printer {

    /// <summary>
    /// Prints from the current position of the current line.
    /// If the statement requires several lines, subsequent lines are indented at "indent".
    /// No newline is printed after the statement.
    /// </summary>
    public void PrintStatement(Statement stmt, int indent, bool removeHints = false) {
      Contract.Requires(stmt != null);

      if (stmt.IsGhost && printMode == PrintModes.NoGhost) { return; }
      for (LList<Label> label = stmt.Labels; label != null; label = label.Next) {
        if (label.Data.Name != null) {
          wr.WriteLine("label {0}:", label.Data.Name);
          Indent(indent);
        }
      }

      if (stmt is PredicateStmt) {
        if (printMode == PrintModes.NoGhost) { return; }
        Expression expr = ((PredicateStmt)stmt).Expr;
        var assertStmt = stmt as AssertStmt;
        var expectStmt = stmt as ExpectStmt;
        if (removeHints) {
          wr.WriteLine("// assert-start");
          Indent(indent);
        }
        wr.Write(assertStmt != null ? "assert" :
                 expectStmt != null ? "expect" :
                 "assume");
        if (stmt.Attributes != null) {
          PrintAttributes(stmt.Attributes);
        }
        wr.Write(" ");
        if (assertStmt != null && assertStmt.Label != null) {
          wr.Write("{0}: ", assertStmt.Label.Name);
        }
        PrintExpression(expr, true);
        if (assertStmt != null && assertStmt.Proof != null) {
          wr.Write(" by ");
          PrintStatement(assertStmt.Proof, indent, false);
        } else if (expectStmt != null && expectStmt.Message != null) {
          wr.Write(", ");
          PrintExpression(expectStmt.Message, true);
          wr.Write(";");
        } else {
          wr.Write(";");
        }
        if (removeHints) {
          wr.WriteLine();
          Indent(indent);
          wr.WriteLine("// assert-end");
        }

      } else if (stmt is PrintStmt) {
        PrintStmt s = (PrintStmt)stmt;
        wr.Write("print");
        PrintAttributeArgs(s.Args, true);
        wr.Write(";");

      } else if (stmt is RevealStmt) {
        var s = (RevealStmt)stmt;
        wr.Write("reveal ");
        var sep = "";
        foreach (var e in s.Exprs) {
          wr.Write(sep);
          sep = ", ";
          if (RevealStmt.SingleName(e) != null) {
            // this will do the printing correctly for labels (or label-lookalikes) like 00_023 (which by PrintExpression below would be printed as 23)
            wr.Write(RevealStmt.SingleName(e));
          } else {
            PrintExpression(e, true);
          }
        }
        wr.Write(";");

      } else if (stmt is BreakStmt) {
        var s = (BreakStmt)stmt;
        if (s.TargetLabel != null) {
          wr.Write($"{s.Kind} {s.TargetLabel.val};");
        } else {
          for (int i = 0; i < s.BreakAndContinueCount - 1; i++) {
            wr.Write("break ");
          }
          wr.Write($"{s.Kind};");
        }

      } else if (stmt is ProduceStmt) {
        var s = (ProduceStmt)stmt;
        wr.Write(s is YieldStmt ? "yield" : "return");
        if (s.Rhss != null) {
          var sep = " ";
          foreach (var rhs in s.Rhss) {
            wr.Write(sep);
            PrintRhs(rhs);
            sep = ", ";
          }
        }
        wr.Write(";");

      } else if (stmt is AssignStmt) {
        AssignStmt s = (AssignStmt)stmt;
        PrintExpression(s.Lhs, true);
        wr.Write(" := ");
        PrintRhs(s.Rhs);
        wr.Write(";");

      } else if (stmt is DividedBlockStmt) {
        var sbs = (DividedBlockStmt)stmt;
        wr.WriteLine("{");
        int ind = indent + IndentAmount;
        foreach (Statement s in sbs.BodyInit) {
          Indent(ind);
          PrintStatement(s, ind, removeHints);
          wr.WriteLine();
        }
        if (sbs.BodyProper.Count != 0 || sbs.SeparatorTok != null) {
          Indent(indent + IndentAmount);
          wr.WriteLine("new;");
          foreach (Statement s in sbs.BodyProper) {
            Indent(ind);
            PrintStatement(s, ind, removeHints);
            wr.WriteLine();
          }
        }
        Indent(indent);
        wr.Write("}");

      } else if (stmt is BlockStmt) {
        wr.WriteLine("{");
        int ind = indent + IndentAmount;
        foreach (Statement s in ((BlockStmt)stmt).Body) {
          Indent(ind);
          PrintStatement(s, ind, removeHints);
          wr.WriteLine();
        }
        Indent(indent);
        wr.Write("}");

      } else if (stmt is IfStmt) {
        IfStmt s = (IfStmt)stmt;
        PrintIfStatement(indent, s, false, removeHints);

      } else if (stmt is AlternativeStmt) {
        var s = (AlternativeStmt)stmt;
        wr.Write("if");
        PrintAttributes(s.Attributes);
        if (s.UsesOptionalBraces) {
          wr.Write(" {");
        }
        PrintAlternatives(indent + (s.UsesOptionalBraces ? IndentAmount : 0), s.Alternatives, removeHints);
        if (s.UsesOptionalBraces) {
          wr.WriteLine();
          Indent(indent);
          wr.Write("}");
        }
      } else if (stmt is WhileStmt) {
        var s = (WhileStmt)stmt;
        PrintWhileStatement(indent, s, false, false, removeHints);
      } else if (stmt is AlternativeLoopStmt) {
        var s = (AlternativeLoopStmt)stmt;
        wr.Write("while");
        PrintAttributes(s.Attributes);
        PrintSpec("invariant", s.Invariants, indent + IndentAmount);
        PrintDecreasesSpec(s.Decreases, indent + IndentAmount);
        PrintFrameSpecLine("modifies", s.Mod, indent + IndentAmount);
        bool hasSpecs = s.Invariants.Count != 0 || (s.Decreases.Expressions != null && s.Decreases.Expressions.Count != 0) || s.Mod.Expressions != null;
        if (s.UsesOptionalBraces) {
          if (hasSpecs) {
            wr.WriteLine();
            Indent(indent);
          } else {
            wr.Write(" ");
          }
          wr.Write("{");
        }
        Contract.Assert(s.Alternatives.Count != 0);
        PrintAlternatives(indent + (s.UsesOptionalBraces ? IndentAmount : 0), s.Alternatives, removeHints);
        if (s.UsesOptionalBraces) {
          wr.WriteLine();
          Indent(indent);
          wr.Write("}");
        }

      } else if (stmt is ForLoopStmt) {
        var s = (ForLoopStmt)stmt;
        PrintForLoopStatement(indent, s, removeHints);

      } else if (stmt is ForallStmt) {
        var s = (ForallStmt)stmt;
        if (options.DafnyPrintResolvedFile != null && s.EffectiveEnsuresClauses != null) {
          foreach (var expr in s.EffectiveEnsuresClauses) {
            PrintExpression(expr, false, new string(' ', indent + IndentAmount) + "ensures ");
          }
          if (s.Body != null) {
            wr.WriteLine();
            Indent(indent);
          }
        } else {
          wr.Write("forall");
          if (s.BoundVars.Count != 0) {
            wr.Write(" ");
            PrintQuantifierDomain(s.BoundVars, s.Attributes, s.Range);
          }
          PrintSpec("ensures", s.Ens, indent + IndentAmount);
          if (s.Body != null) {
            if (s.Ens.Count == 0) {
              wr.Write(" ");
            } else {
              wr.WriteLine();
              Indent(indent);
            }
          }
        }
        if (s.Body != null) {
          PrintStatement(s.Body, indent, removeHints);
        }

      } else if (stmt is ModifyStmt) {
        var s = (ModifyStmt)stmt;
        PrintModifyStmt(indent, s, false);

      } else if (stmt is CalcStmt) {
        CalcStmt s = (CalcStmt)stmt;
        if (printMode == PrintModes.NoGhost) { return; }   // Calcs don't get a "ghost" attribute, but they are.
        wr.Write("calc");
        PrintAttributes(stmt.Attributes);
        wr.Write(" ");
        if (s.UserSuppliedOp != null) {
          PrintCalcOp(s.UserSuppliedOp);
          wr.Write(" ");
        } else if (options.DafnyPrintResolvedFile != null && s.Op != null) {
          PrintCalcOp(s.Op);
          wr.Write(" ");
        }
        wr.WriteLine("{");
        int lineInd = indent + IndentAmount;
        int lineCount = s.Lines.Count == 0 ? 0 : s.Lines.Count - 1;  // if nonempty, .Lines always contains a duplicated last line
        // The number of op/hints is commonly one less than the number of lines, but
        // it can also equal the number of lines for empty calc's and for calc's with
        // a dangling hint.
        int hintCount = s.Lines.Count != 0 && s.Hints.Last().Body.Count == 0 ? lineCount - 1 : lineCount;
        for (var i = 0; i < lineCount; i++) {
          var e = s.Lines[i];
          var op = s.StepOps[i];
          var h = s.Hints[i];
          // print the line
          Indent(lineInd);
          PrintExpression(e, true, lineInd);
          wr.WriteLine(";");
          if (i == hintCount) {
            break;
          }
          // print the operator, if any
          if (op != null || (options.DafnyPrintResolvedFile != null && s.Op != null)) {
            Indent(indent);  // this lines up with the "calc"
            PrintCalcOp(op ?? s.Op);
            wr.WriteLine();
          }
          // print the hints
          foreach (var st in h.Body) {
            Indent(lineInd);
            PrintStatement(st, lineInd, false);
            wr.WriteLine();
          }
        }
        Indent(indent);
        wr.Write("}");
      } else if (stmt is NestedMatchStmt) {
        // Print ResolvedStatement, if present, as comment
        var s = (NestedMatchStmt)stmt;

        if (s.Flattened != null && options.DafnyPrintResolvedFile != null) {
          wr.WriteLine();
          if (!printingDesugared) {
            Indent(indent); wr.WriteLine("/*---------- flattened ----------");
          }

          var savedDesugarMode = printingDesugared;
          printingDesugared = true;
          Indent(indent); PrintStatement(s.Flattened, indent, removeHints);
          wr.WriteLine();
          printingDesugared = savedDesugarMode;

          if (!printingDesugared) {
            Indent(indent); wr.WriteLine("---------- end flattened ----------*/");
          }
          Indent(indent);
        }

        if (!printingDesugared) {
          wr.Write("match");
          PrintAttributes(s.Attributes);
          wr.Write(" ");
          PrintExpression(s.Source, false);
          if (s.UsesOptionalBraces) {
            wr.Write(" {");
          }
          int caseInd = indent + (s.UsesOptionalBraces ? IndentAmount : 0);
          foreach (NestedMatchCaseStmt mc in s.Cases) {
            wr.WriteLine();
            Indent(caseInd);
            wr.Write("case");
            PrintAttributes(mc.Attributes);
            wr.Write(" ");
            PrintExtendedPattern(mc.Pat);
            wr.Write(" =>");
            foreach (Statement bs in mc.Body) {
              wr.WriteLine();
              Indent(caseInd + IndentAmount);
              PrintStatement(bs, caseInd + IndentAmount, removeHints);
            }
          }
          if (s.UsesOptionalBraces) {
            wr.WriteLine();
            Indent(indent);
            wr.Write("}");
          }
        }
      } else if (stmt is MatchStmt) {
        var s = (MatchStmt)stmt;
        wr.Write("match");
        PrintAttributes(s.Attributes);
        wr.Write(" ");
        PrintExpression(s.Source, false);
        if (s.UsesOptionalBraces) {
          wr.Write(" {");
        }

        int caseInd = indent + (s.UsesOptionalBraces ? IndentAmount : 0);
        foreach (MatchCaseStmt mc in s.Cases) {
          wr.WriteLine();
          Indent(caseInd);
          wr.Write("case");
          PrintAttributes(mc.Attributes);
          wr.Write(" ");
          if (!mc.Ctor.Name.StartsWith(SystemModuleManager.TupleTypeCtorNamePrefix)) {
            wr.Write(mc.Ctor.Name);
          }

          PrintMatchCaseArgument(mc);
          wr.Write(" =>");
          foreach (Statement bs in mc.Body) {
            wr.WriteLine();
            Indent(caseInd + IndentAmount);
            PrintStatement(bs, caseInd + IndentAmount, removeHints);
          }
        }

        if (s.UsesOptionalBraces) {
          wr.WriteLine();
          Indent(indent);
          wr.Write("}");
        }

      } else if (stmt is ConcreteUpdateStatement) {
        var s = (ConcreteUpdateStatement)stmt;
        string sep = "";
        foreach (var lhs in s.Lhss) {
          wr.Write(sep);
          PrintExpression(lhs, true);
          sep = ", ";
        }
        if (s.Lhss.Count > 0) {
          wr.Write(" ");
        }
        bool isLemmaPrint = false;
        if (s is UpdateStmt) { 
          var s1 = (UpdateStmt)s;
          if (s1.Lhss.Count == 0 && s1.Rhss.Count == 1) {
            var rhs = s1.Rhss[0];
            if (rhs is ExprRhs) {
              var expr = ((ExprRhs)rhs).Expr;
              if (expr is ApplySuffix) {
                var e = (ApplySuffix)expr;
                string name = e.Lhs is NameSegment ? ((NameSegment)e.Lhs).Name : e.Lhs is ExprDotName ? ((ExprDotName)e.Lhs).SuffixName : null;
                if (name != null && this.lemmas.Contains(name)) {
                  isLemmaPrint = removeHints;
                }
              }
            }
          }
        }
        if (isLemmaPrint) {
          wr.WriteLine("// assert-start");
          Indent(indent);
        }
        PrintUpdateRHS(s, indent, removeHints);
        wr.Write(";");
        if (isLemmaPrint) {
          wr.WriteLine();
          Indent(indent);
          wr.WriteLine("// assert-end");
        }
      } else if (stmt is CallStmt) {
        // Most calls are printed from their concrete syntax given in the input. However, recursive calls to
        // prefix lemmas end up as CallStmt's by the end of resolution and they may need to be printed here.
        var s = (CallStmt)stmt;
        bool comment = removeHints && (s.Method is ExtremeLemma || s.Method is PrefixLemma || s.Method is Lemma || s.Method is TwoStateLemma);
        if (comment) {
          wr.WriteLine("// assert-start");
          Indent(indent);
        }
        PrintExpression(s.MethodSelect, false);
        PrintActualArguments(s.Bindings, s.Method.Name, null);
        if (comment) {
          Indent(indent);
          wr.WriteLine("// assert-end");
        }

      } else if (stmt is VarDeclStmt) {
        var s = (VarDeclStmt)stmt;
        if (s.Locals.Exists(v => v.IsGhost) && printMode == PrintModes.NoGhost) { return; }
        if (s.Locals.TrueForAll((v => v.IsGhost))) {
          // Emit the "ghost" modifier if all of the variables are ghost. If some are ghost, but not others,
          // then some of these ghosts are auto-converted to ghost, so we should not emit the "ghost" keyword.
          wr.Write("ghost ");
        }
        wr.Write("var");
        string sep = "";
        foreach (var local in s.Locals) {
          wr.Write(sep);
          if (local.Attributes != null) {
            PrintAttributes(local.Attributes);
          }
          wr.Write(" {0}", local.DisplayName);
          PrintType(": ", local.SyntacticType);
          sep = ",";
        }
        if (s.Update != null) {
          wr.Write(" ");
          PrintUpdateRHS(s.Update, indent);
        }
        wr.Write(";");

      } else if (stmt is VarDeclPattern) {
        var s = (VarDeclPattern)stmt;
        if (s.tok is AutoGeneratedToken) {
          wr.Write("/* ");
        }
        if (s.HasGhostModifier) {
          wr.Write("ghost ");
        }
        wr.Write("var ");
        PrintCasePattern(s.LHS);
        wr.Write(" := ");
        PrintExpression(s.RHS, true);
        wr.Write(";");
        if (s.tok is AutoGeneratedToken) {
          wr.Write(" */");
        }

      } else if (stmt is SkeletonStatement) {
        var s = (SkeletonStatement)stmt;
        if (s.S == null) {
          wr.Write("...;");
        } else if (s.S is AssertStmt) {
          Contract.Assert(s.ConditionOmitted);
          wr.Write("assert ...;");
        } else if (s.S is ExpectStmt) {
          Contract.Assert(s.ConditionOmitted);
          wr.Write("expect ...;");
        } else if (s.S is AssumeStmt) {
          Contract.Assert(s.ConditionOmitted);
          wr.Write("assume ...;");
        } else if (s.S is IfStmt) {
          PrintIfStatement(indent, (IfStmt)s.S, s.ConditionOmitted, removeHints);
        } else if (s.S is WhileStmt) {
          PrintWhileStatement(indent, (WhileStmt)s.S, s.ConditionOmitted, s.BodyOmitted, removeHints);
        } else if (s.S is ModifyStmt) {
          PrintModifyStmt(indent, (ModifyStmt)s.S, true);
        } else {
          Contract.Assert(false);
          throw new cce.UnreachableException(); // unexpected skeleton statement
        }

      } else if (stmt is TryRecoverStatement haltRecoveryStatement) {
        // These have no actual syntax for Dafny user code, so emit something
        // clearly not parsable.
        int ind = indent + IndentAmount;

        Indent(indent);
        wr.WriteLine("[[ try { ]]");
        PrintStatement(haltRecoveryStatement.TryBody, ind, removeHints);
        wr.WriteLine();

        Indent(indent);
        wr.WriteLine($"[[ }} recover ({haltRecoveryStatement.HaltMessageVar.Name}) {{ ]]");
        PrintStatement(haltRecoveryStatement.RecoverBody, ind, removeHints);
        wr.Write("[[ } ]]");
      } else {
        Contract.Assert(false); throw new cce.UnreachableException();  // unexpected statement
      }
    }

    private void PrintModifyStmt(int indent, ModifyStmt s, bool omitFrame) {
      Contract.Requires(0 <= indent);
      Contract.Requires(s != null);
      Contract.Requires(!omitFrame || s.Mod.Expressions.Count == 0);

      wr.Write("modify");
      PrintAttributes(s.Mod.Attributes);
      wr.Write(" ");
      if (omitFrame) {
        wr.Write("...");
      } else {
        PrintFrameExpressionList(s.Mod.Expressions);
      }
      if (s.Body != null) {
        // There's a possible syntactic ambiguity, namely if the frame is empty (more precisely,
        // if s.Mod.Expressions.Count is 0).  Since the statement was parsed at some point, this
        // situation can occur only if the modify statement inherited its frame by refinement
        // and we're printing the post-resolve AST.  In this special case, print an explicit
        // empty set as the frame.
        if (s.Mod.Expressions.Count == 0) {
          wr.Write(" {}");
        }
        wr.Write(" ");
        PrintStatement(s.Body, indent, false);
      } else {
        wr.Write(";");
      }
    }

    /// <summary>
    /// Does not print LHS, nor the space one might want between LHS and RHS,
    /// because if there's no LHS, we don't want to start with a space
    /// </summary>
    void PrintUpdateRHS(ConcreteUpdateStatement s, int indent, bool removeHints = false) {
      Contract.Requires(s != null);
      if (s is UpdateStmt) {
        var update = (UpdateStmt)s;
        if (update.Lhss.Count != 0) {
          wr.Write(":= ");
        }
        var sep = "";
        foreach (var rhs in update.Rhss) {
          wr.Write(sep);
          PrintRhs(rhs, removeHints, indent);
          sep = ", ";
        }
      } else if (s is AssignSuchThatStmt) {
        var update = (AssignSuchThatStmt)s;
        wr.Write(":| ");
        if (update.AssumeToken != null) {
          wr.Write("assume");
          PrintAttributes(update.AssumeToken.Attrs);
          wr.Write(" ");
        }
        PrintExpression(update.Expr, true);
      } else if (s is AssignOrReturnStmt) {
        var stmt = (AssignOrReturnStmt)s;
        wr.Write(":-");
        if (stmt.KeywordToken != null) {
          wr.Write($" {stmt.KeywordToken.Token.val}");
          PrintAttributes(stmt.KeywordToken.Attrs);
        }
        wr.Write(" ");
        PrintRhs(stmt.Rhs);
        foreach (var rhs in stmt.Rhss) {
          wr.Write(", ");
          PrintRhs(rhs);
        }
        if (options.DafnyPrintResolvedFile != null && stmt.ResolvedStatements.Count > 0) {
          wr.WriteLine();
          Indent(indent); wr.WriteLine("/*---------- desugared ----------");
          foreach (Statement r in stmt.ResolvedStatements) {
            Indent(indent);
            PrintStatement(r, indent, false);
            wr.WriteLine();
          }
          Indent(indent); wr.Write("---------- end desugared ----------*/");
        }

      } else {
        Contract.Assert(false);  // otherwise, unknown type
      }
    }

    void PrintIfStatement(int indent, IfStmt s, bool omitGuard, bool removeHints) {
      wr.Write("if");
      PrintAttributes(s.Attributes);
      wr.Write(" ");
      if (omitGuard) {
        wr.Write("... ");
      } else {
        PrintGuard(s.IsBindingGuard, s.Guard);
        wr.Write(" ");
      }
      PrintStatement(s.Thn, indent, removeHints);
      if (s.Els != null) {
        wr.Write(" else");
        if (!(s.Els is IfStmt) && s.Els.Attributes != null) {
          PrintAttributes(s.Els.Attributes);
        }
        wr.Write(" ");
        PrintStatement(s.Els, indent, removeHints);
      }
    }

    void PrintWhileStatement(int indent, WhileStmt s, bool omitGuard, bool omitBody, bool removeHints) {
      Contract.Requires(0 <= indent);
      wr.Write("while");
      PrintAttributes(s.Attributes);
      wr.Write(" ");
      if (omitGuard) {
        wr.Write("...");
      } else {
        PrintGuard(false, s.Guard);
      }

      if (removeHints) {
        wr.WriteLine();
        Indent(indent + IndentAmount);
        wr.WriteLine("// invariants-start");
      }
      PrintSpec("invariant", s.Invariants, indent + IndentAmount);
      PrintDecreasesSpec(s.Decreases, indent + IndentAmount);
      PrintFrameSpecLine("modifies", s.Mod, indent + IndentAmount);
      if (removeHints) {
        wr.WriteLine();
        Indent(indent + IndentAmount);
        wr.WriteLine("// invariants-end");
      }
      if (omitBody) {
        wr.WriteLine();
        Indent(indent + IndentAmount);
        wr.Write("...;");
      } else if (s.Body != null) {
        if (s.Invariants.Count == 0 && s.Decreases.Expressions.Count == 0 && (s.Mod.Expressions == null || s.Mod.Expressions.Count == 0)) {
          wr.Write(" ");
        } else {
          wr.WriteLine();
          Indent(indent);
        }
        PrintStatement(s.Body, indent,removeHints);
      }
    }

    void PrintAlternatives(int indent, List<GuardedAlternative> alternatives, bool removeHints) {
      var startWithLine = true;
      foreach (var alternative in alternatives) {
        if (startWithLine) {
          wr.WriteLine();
        } else {
          startWithLine = true;
        }
        Indent(indent);
        wr.Write("case");
        PrintAttributes(alternative.Attributes);
        wr.Write(" ");
        if (alternative.IsBindingGuard) {
          var exists = (ExistsExpr)alternative.Guard;
          PrintBindingGuard(exists);
        } else {
          PrintExpression(alternative.Guard, false);
        }
        wr.Write(" =>");
        foreach (Statement s in alternative.Body) {
          wr.WriteLine();
          Indent(indent + IndentAmount);
          PrintStatement(s, indent + IndentAmount, removeHints);
        }
      }
    }

    void PrintForLoopStatement(int indent, ForLoopStmt s, bool removeHints) {
      Contract.Requires(0 <= indent);
      Contract.Requires(s != null);
      wr.Write("for");
      PrintAttributes(s.Attributes);
      wr.Write($" {s.LoopIndex.Name}");
      PrintType(": ", s.LoopIndex.Type);
      wr.Write(" := ");
      PrintExpression(s.Start, false);
      wr.Write(s.GoingUp ? " to " : " downto ");
      if (s.End == null) {
        wr.Write("*");
      } else {
        PrintExpression(s.End, false);
      }

      if (removeHints) {
        wr.WriteLine();
        Indent(indent + IndentAmount);
        wr.WriteLine("// invariants-start");
      }
      PrintSpec("invariant", s.Invariants, indent + IndentAmount);
      PrintDecreasesSpec(s.Decreases, indent + IndentAmount);
      if (s.Mod.Expressions != null) {
        PrintFrameSpecLine("modifies", s.Mod, indent + IndentAmount);
      }
      if (removeHints) {
        wr.WriteLine();
        Indent(indent + IndentAmount);
        wr.WriteLine("// invariants-end");
      }
      if (s.Body != null) {
        if (s.Invariants.Count == 0 && s.Decreases.Expressions.Count == 0 && (s.Mod.Expressions == null || s.Mod.Expressions.Count == 0)) {
          wr.Write(" ");
        } else {
          wr.WriteLine();
          Indent(indent);
        }
        PrintStatement(s.Body, indent,removeHints);
      }
    }

    void PrintRhs(AssignmentRhs rhs, bool removeHints = false, int indent = -1) {
      Contract.Requires(rhs != null);
      if (rhs is ExprRhs) {
        PrintExpression(((ExprRhs)rhs).Expr, true);
      } else if (rhs is HavocRhs) {
        wr.Write("*");
      } else if (rhs is TypeRhs) {
        TypeRhs t = (TypeRhs)rhs;
        wr.Write("new ");
        if (t.ArrayDimensions != null) {
          if (ShowType(t.EType)) {
            PrintType(t.EType);
          }
          if (options.DafnyPrintResolvedFile == null &&
            t.InitDisplay != null && t.ArrayDimensions.Count == 1 &&
            AutoGeneratedToken.Is(t.ArrayDimensions[0].tok)) {
            // elide the size
            wr.Write("[]");
          } else {
            string s = "[";
            foreach (Expression dim in t.ArrayDimensions) {
              Contract.Assume(dim != null);
              wr.Write(s);
              PrintExpression(dim, false);
              s = ", ";
            }
            wr.Write("]");
          }
          if (t.ElementInit != null) {
            wr.Write(" (");
            PrintExpression(t.ElementInit, false);
            wr.Write(")");
          } else if (t.InitDisplay != null) {
            wr.Write(" [");
            PrintExpressionList(t.InitDisplay, false);
            wr.Write("]");
          }
        } else if (t.Bindings == null) {
          PrintType(t.EType);
        } else {
          PrintType(t.Path);
          wr.Write("(");
          PrintBindings(t.Bindings, false);
          wr.Write(")");
        }
      } else {
        Contract.Assert(false); throw new cce.UnreachableException();  // unexpected RHS
      }

      if (rhs.HasAttributes()) {
        PrintAttributes(rhs.Attributes);
      }
    }

    void PrintGuard(bool isBindingGuard, Expression guard) {
      Contract.Requires(!isBindingGuard || (guard is ExistsExpr && ((ExistsExpr)guard).Range == null));
      if (guard == null) {
        wr.Write("*");
      } else if (isBindingGuard) {
        var exists = (ExistsExpr)guard;
        PrintBindingGuard(exists);
      } else {
        PrintExpression(guard, false);
      }
    }

    void PrintBindingGuard(ExistsExpr guard) {
      Contract.Requires(guard != null);
      Contract.Requires(guard.Range == null);
      PrintQuantifierDomain(guard.BoundVars, guard.Attributes, null);
      wr.Write(" :| ");
      PrintExpression(guard.Term, false);
    }

    void PrintCalcOp(CalcStmt.CalcOp op) {
      Contract.Requires(op != null);
      wr.Write(op.ToString());
      if (op is CalcStmt.TernaryCalcOp) {
        wr.Write("[");
        PrintExpression(((CalcStmt.TernaryCalcOp)op).Index, false);
        wr.Write("]");
      }
    }
  }
}
