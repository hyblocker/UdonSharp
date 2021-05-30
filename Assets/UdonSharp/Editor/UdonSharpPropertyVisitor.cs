using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharp.Compiler
{
    public class PropertyVisitor : UdonSharpSyntaxWalker
    {
        public List<PropertyDefinition> definedProperties = new List<PropertyDefinition>();

        public PropertyVisitor(ResolverContext resolver, SymbolTable rootTable, LabelTable labelTable)
            : base(UdonSharpSyntaxWalkerDepth.ClassDefinitions, resolver, rootTable, labelTable) { }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            UpdateSyntaxNode(node);

            PropertyDefinition propertyDefinition = new PropertyDefinition();

            propertyDefinition.declarationFlags = node.Modifiers.HasModifier("public") ? PropertyDeclFlags.Public : PropertyDeclFlags.Private;
            propertyDefinition.originalPropertyName = node.Identifier.Text;

            Type type = null;

            using (ExpressionCaptureScope propertyTypeCapture = new ExpressionCaptureScope(visitorContext, null))
            {
                Visit(node.Type);

                type = propertyTypeCapture.captureType;

                if (!visitorContext.resolverContext.IsValidUdonType(type))
                    throw new NotSupportedException($"Udon does not support variable of type '{type}' yet");
            }

            BackingFieldDefinition backingField = new BackingFieldDefinition();
            backingField.backingFieldName = $"{node.Identifier.Text}_k_BackingField";

            backingField.type = type;
            propertyDefinition.type = type;
            backingField.type = type;

            if (node.AccessorList != null)
            {
                foreach (AccessorDeclarationSyntax accessor in node.AccessorList.Accessors)
                {
                    bool isSetter = accessor.Keyword.Kind() == SyntaxKind.SetKeyword;
                    if (isSetter)
                    {
                        SetterDefinition setter = new SetterDefinition();
                        setter.accessorName = $"set_{node.Identifier.Text}";
                        setter.entryPoint = visitorContext.labelTable.GetNewJumpLabel("udonMethodEntryPoint");
                        setter.userCallStart = visitorContext.labelTable.GetNewJumpLabel("userMethodCallEntry");
                        setter.returnPoint = visitorContext.labelTable.GetNewJumpLabel("methodReturnPoint");
                        setter.backingField = (accessor.Body == null && accessor.ExpressionBody == null) ? backingField : null;
                        setter.type = type;
                        setter.declarationFlags = GetDeclFlags(accessor);
                        setter.paramSymbol = visitorContext.topTable.CreateNamedSymbol("value", type, SymbolDeclTypeFlags.Local | SymbolDeclTypeFlags.MethodParameter);

                        propertyDefinition.setter = setter;
                    }
                    else
                    {
                        GetterDefinition getter = new GetterDefinition();
                        getter.accessorName = $"get_{node.Identifier.Text}";
                        getter.entryPoint = visitorContext.labelTable.GetNewJumpLabel("udonMethodEntryPoint");
                        getter.userCallStart = visitorContext.labelTable.GetNewJumpLabel("userMethodCallEntry");
                        getter.returnPoint = visitorContext.labelTable.GetNewJumpLabel("methodReturnPoint");
                        getter.backingField = (accessor.Body == null && accessor.ExpressionBody == null) ? backingField : null;
                        getter.type = type;
                        getter.declarationFlags = GetDeclFlags(accessor);

                        using (ExpressionCaptureScope returnTypeCapture = new ExpressionCaptureScope(visitorContext, null))
                        {
                            Visit(node.Type);

                            getter.returnSymbol = visitorContext.topTable.CreateNamedSymbol("returnValSymbol", returnTypeCapture.captureType, SymbolDeclTypeFlags.Internal);
                        }

                        propertyDefinition.getter = getter;
                    }
                }
            }
            else
            {
                GetterDefinition getter = new GetterDefinition();
                getter.accessorName = $"get_{node.Identifier.Text}";
                getter.entryPoint = visitorContext.labelTable.GetNewJumpLabel("udonMethodEntryPoint");
                getter.userCallStart = visitorContext.labelTable.GetNewJumpLabel("userMethodCallEntry");
                getter.returnPoint = visitorContext.labelTable.GetNewJumpLabel("methodReturnPoint");
                getter.type = type;
                getter.declarationFlags = PropertyDeclFlags.Public;

                using (ExpressionCaptureScope returnTypeCapture = new ExpressionCaptureScope(visitorContext, null))
                {
                    Visit(node.Type);

                    getter.returnSymbol = visitorContext.topTable.CreateNamedSymbol("returnValSymbol", returnTypeCapture.captureType, SymbolDeclTypeFlags.Internal);
                }

                propertyDefinition.getter = getter;
            }

            if (propertyDefinition.setter?.backingField != null || propertyDefinition.getter?.backingField != null)
                backingField.fieldSymbol = visitorContext.topTable.CreateNamedSymbol("kBackingField", type, SymbolDeclTypeFlags.Internal | SymbolDeclTypeFlags.PropertyBackingField);

            definedProperties.Add(propertyDefinition);
        }

        private PropertyDeclFlags GetDeclFlags(AccessorDeclarationSyntax accessor)
        {
            if (accessor.Modifiers.Any())
            {
                if (accessor.Modifiers.HasModifier("public"))
                    return PropertyDeclFlags.Public;

                return PropertyDeclFlags.Private;
            }

            return PropertyDeclFlags.None;
        }
    }
}