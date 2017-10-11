// 
// Copyright (c) 2004-2017 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

namespace NLog.Conditions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using NLog.Common;

    /// <summary>
    /// Condition method invocation expression (represented by <b>method(p1,p2,p3)</b> syntax).
    /// </summary>
	internal sealed class ConditionMethodExpression : ConditionExpression
    {
        private readonly bool _acceptsLogEvent;
        private readonly string _conditionMethodName;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConditionMethodExpression" /> class.
        /// </summary>
        /// <param name="conditionMethodName">Name of the condition method.</param>
        /// <param name="methodInfo"><see cref="MethodInfo"/> of the condition method.</param>
        /// <param name="methodParameters">The method parameters.</param>
        public ConditionMethodExpression(string conditionMethodName, MethodInfo methodInfo, IEnumerable<ConditionExpression> methodParameters)
        {
            this.MethodInfo = methodInfo;
            this._conditionMethodName = conditionMethodName;
            this.MethodParameters = new List<ConditionExpression>(methodParameters).AsReadOnly();

            ParameterInfo[] formalParameters = this.MethodInfo.GetParameters();
            if (formalParameters.Length > 0 && formalParameters[0].ParameterType == typeof(LogEventInfo))
            {
                this._acceptsLogEvent = true;
            }

            int actualParameterCount = this.MethodParameters.Count;
            if (this._acceptsLogEvent)
            {
                actualParameterCount++;
            }

            // Count the number of required and optional parameters
            int requiredParametersCount = 0;
            int optionalParametersCount = 0;

            foreach ( var param in formalParameters )
            {
                if ( param.IsOptional )
                    ++optionalParametersCount;
                else
                    ++requiredParametersCount;
            }

            if ( !( ( actualParameterCount >= requiredParametersCount ) && ( actualParameterCount <= formalParameters.Length ) ) )
            {
                string message;

                if ( optionalParametersCount > 0 )
                {
                    message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Condition method '{0}' requires between {1} and {2} parameters, but passed {3}.",
                        conditionMethodName,
                        requiredParametersCount,
                        formalParameters.Length,
                        actualParameterCount );
                }
                else
                {
                    message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Condition method '{0}' requires {1} parameters, but passed {2}.",
                        conditionMethodName,
                        requiredParametersCount,
                        actualParameterCount );
                }
                InternalLogger.Error(message);
                throw new ConditionParseException(message);
            }

            this._lateBoundMethod = Internal.ReflectionHelpers.CreateLateBoundMethod(MethodInfo);
            if (formalParameters.Length > MethodParameters.Count)
            {
                this._lateBoundMethodDefaultParameters = new object[formalParameters.Length - MethodParameters.Count];
                for (int i = MethodParameters.Count; i < formalParameters.Length; ++i)
                {
                    ParameterInfo param = formalParameters[i];
                    this._lateBoundMethodDefaultParameters[i - MethodParameters.Count] = param.DefaultValue;
                }
            }
            else
            {
                this._lateBoundMethodDefaultParameters = null;
            }
        }

        /// <summary>
        /// Gets the method info.
        /// </summary>
        public MethodInfo MethodInfo { get; private set; }
        private readonly Internal.ReflectionHelpers.LateBoundMethod _lateBoundMethod;
        private readonly object[] _lateBoundMethodDefaultParameters;

        /// <summary>
        /// Gets the method parameters.
        /// </summary>
        /// <value>The method parameters.</value>
        public IList<ConditionExpression> MethodParameters { get; private set; }

        /// <summary>
        /// Returns a string representation of the expression.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the condition expression.
        /// </returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(this._conditionMethodName);
            sb.Append("(");

            string separator = string.Empty;

            //Memory profiling pointed out that using a foreach-loop was allocating
            //an Enumerator. Switching to a for-loop avoids the memory allocation.
            for (int i = 0; i < this.MethodParameters.Count; i++)
            {
                ConditionExpression expr = this.MethodParameters[i];
                sb.Append(separator);
                sb.Append(expr);
                separator = ", ";
            }

            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// Evaluates the expression.
        /// </summary>
        /// <param name="context">Evaluation context.</param>
        /// <returns>Expression result.</returns>
        protected override object EvaluateNode(LogEventInfo context)
        {
            int parameterOffset = this._acceptsLogEvent ? 1 : 0;
            int parameterDefaults = this._lateBoundMethodDefaultParameters != null ? this._lateBoundMethodDefaultParameters.Length : 0;

            var callParameters = new object[this.MethodParameters.Count + parameterOffset + parameterDefaults];

            //Memory profiling pointed out that using a foreach-loop was allocating
            //an Enumerator. Switching to a for-loop avoids the memory allocation.
            for (int i = 0; i < this.MethodParameters.Count; i++)
            {
                ConditionExpression ce = this.MethodParameters[i];
                callParameters[i + parameterOffset] = ce.Evaluate(context);
            }

            if (this._acceptsLogEvent)
            {
                callParameters[0] = context;
            }

            if (this._lateBoundMethodDefaultParameters != null)
            {
                for (int i = this._lateBoundMethodDefaultParameters.Length - 1; i >= 0; --i)
                {
                    callParameters[callParameters.Length - i - 1] = this._lateBoundMethodDefaultParameters[i];
                }
            }

            return this._lateBoundMethod(null, callParameters);  // Static-method so object-instance = null
        }
    }
}