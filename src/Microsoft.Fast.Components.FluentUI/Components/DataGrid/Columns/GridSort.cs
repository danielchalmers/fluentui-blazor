// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq.Expressions;

namespace Microsoft.Fast.Components.FluentUI;

/// <summary>
/// Represents a sort order specification used within <see cref="FluentDataGrid{TGridItem}"/>.
/// </summary>
/// <typeparam name="TGridItem">The type of data represented by each row in the grid.</typeparam>
public class GridSort<TGridItem>
{
    private const string ExpressionNotRepresentableMessage = "The supplied expression can't be represented as a property name for sorting. Only simple member expressions, such as @(x => x.SomeProperty), can be converted to property names.";

    private Func<IQueryable<TGridItem>, bool, IOrderedQueryable<TGridItem>> _first;
    private List<Func<IOrderedQueryable<TGridItem>, bool, IOrderedQueryable<TGridItem>>>? _then;

    private (LambdaExpression, bool) _firstExpression;
    private List<(LambdaExpression, bool)>? _thenExpressions;

    private IReadOnlyCollection<(string PropertyName, SortDirection Direction)>? _cachedPropertyListAscending;
    private IReadOnlyCollection<(string PropertyName, SortDirection Direction)>? _cachedPropertyListDescending;

    internal GridSort(Func<IQueryable<TGridItem>, bool, IOrderedQueryable<TGridItem>> first, (LambdaExpression, bool) firstExpression)
    {
        _first = first;
        _firstExpression = firstExpression;
        _then = default;
        _thenExpressions = default;
    }

    /// <summary>
    /// Produces a <see cref="GridSort{T}"/> instance that sorts according to the specified <paramref name="expression"/>, ascending.
    /// </summary>
    /// <typeparam name="U">The type of the expression's value.</typeparam>
    /// <param name="expression">An expression defining how a set of <typeparamref name="TGridItem"/> instances are to be sorted.</param>
    /// <returns>A <see cref="GridSort{T}"/> instance representing the specified sorting rule.</returns>
    public static GridSort<TGridItem> ByAscending<U>(Expression<Func<TGridItem, U>> expression)
        => new GridSort<TGridItem>((queryable, asc) => asc ? queryable.OrderBy(expression) : queryable.OrderByDescending(expression),
            (expression, true));

    /// <summary>
    /// Produces a <see cref="GridSort{T}"/> instance that sorts according to the specified <paramref name="expression"/> 
    /// using the specified <paramref name="comparer"/>, ascending.
    /// </summary>
    /// <typeparam name="U">The type of the expression's value.</typeparam>
    /// <param name="expression">An expression defining how a set of <typeparamref name="TGridItem"/> instances are to be sorted.</param>
    /// <param name="comparer">Defines how a items in a set of <typeparamref name="TGridItem"/> instances are to be compared.</param>
    /// <returns>A <see cref="GridSort{T}"/> instance representing the specified sorting rule.</returns>
    public static GridSort<TGridItem> ByAscending<U>(Expression<Func<TGridItem, U>> expression, IComparer<U> comparer)
        => new GridSort<TGridItem>((queryable, asc) => asc ? queryable.OrderBy(expression, comparer) : queryable.OrderByDescending(expression, comparer),
            (expression, true));

    /// <summary>
    /// Produces a <see cref="GridSort{T}"/> instance that sorts according to the specified <paramref name="expression"/>, descending.
    /// </summary>
    /// <typeparam name="U">The type of the expression's value.</typeparam>
    /// <param name="expression">An expression defining how a set of <typeparamref name="TGridItem"/> instances are to be sorted.</param>
    /// <returns>A <see cref="GridSort{T}"/> instance representing the specified sorting rule.</returns>
    public static GridSort<TGridItem> ByDescending<U>(Expression<Func<TGridItem, U>> expression)
        => new GridSort<TGridItem>((queryable, asc) => asc ? queryable.OrderByDescending(expression) : queryable.OrderBy(expression),
            (expression, false));

    /// <summary>
    /// Updates a <see cref="GridSort{T}"/> instance by appending a further sorting rule.
    /// </summary>
    /// <typeparam name="U">The type of the expression's value.</typeparam>
    /// <param name="expression">An expression defining how a set of <typeparamref name="TGridItem"/> instances are to be sorted.</param>
    /// <returns>A <see cref="GridSort{T}"/> instance representing the specified sorting rule.</returns>
    public GridSort<TGridItem> ThenAscending<U>(Expression<Func<TGridItem, U>> expression)
    {
        _then ??= new();
        _thenExpressions ??= new();
        _then.Add((queryable, asc) => asc ? queryable.ThenBy(expression) : queryable.ThenByDescending(expression));
        _thenExpressions.Add((expression, true));
        _cachedPropertyListAscending = null;
        _cachedPropertyListDescending = null;
        return this;
    }

    /// <summary>
    /// Updates a <see cref="GridSort{T}"/> instance by appending a further sorting rule.
    /// </summary>
    /// <typeparam name="U">The type of the expression's value.</typeparam>
    /// <param name="expression">An expression defining how a set of <typeparamref name="TGridItem"/> instances are to be sorted.</param>
    /// <returns>A <see cref="GridSort{T}"/> instance representing the specified sorting rule.</returns>
    public GridSort<TGridItem> ThenDescending<U>(Expression<Func<TGridItem, U>> expression)
    {
        _then ??= new();
        _thenExpressions ??= new();
        _then.Add((queryable, asc) => asc ? queryable.ThenByDescending(expression) : queryable.ThenBy(expression));
        _thenExpressions.Add((expression, false));
        _cachedPropertyListAscending = null;
        _cachedPropertyListDescending = null;
        return this;
    }

    internal IOrderedQueryable<TGridItem> Apply(IQueryable<TGridItem> queryable, bool ascending)
    {
        var orderedQueryable = _first(queryable, ascending);

        if (_then is not null)
        {
            foreach (var clause in _then)
            {
                orderedQueryable = clause(orderedQueryable, ascending);
            }
        }

        return orderedQueryable;
    }

    internal IReadOnlyCollection<(string PropertyName, SortDirection Direction)> ToPropertyList(bool ascending)
    {
        if (ascending)
        {
            _cachedPropertyListAscending ??= BuildPropertyList(ascending: true);
            return _cachedPropertyListAscending;
        }
        else
        {
            _cachedPropertyListDescending ??= BuildPropertyList(ascending: false);
            return _cachedPropertyListDescending;
        }
    }

    private IReadOnlyCollection<(string PropertyName, SortDirection Direction)> BuildPropertyList(bool ascending)
    {
        var result = new List<(string, SortDirection)>();
        result.Add((ToPropertyName(_firstExpression.Item1), (_firstExpression.Item2 ^ ascending) ? SortDirection.Descending : SortDirection.Ascending));

        if (_thenExpressions is not null)
        {
            foreach (var (thenLambda, thenAscending) in _thenExpressions)
            {
                result.Add((ToPropertyName(thenLambda), (thenAscending ^ ascending) ? SortDirection.Descending : SortDirection.Ascending));
            }
        }

        return result;
    }

    // Not sure we really want this level of complexity, but it converts expressions like @(c => c.Medals.Gold) to "Medals.Gold"
    private static string ToPropertyName(LambdaExpression expression)
    {
        var body = expression.Body as MemberExpression;
        if (body is null)
        {
            throw new ArgumentException(ExpressionNotRepresentableMessage);
        }

        // Handles cases like @(x => x.Name)
        if (body.Expression is ParameterExpression)
        {
            return body.Member.Name;
        }

        // First work out the length of the string we'll need, so that we can use string.Create
        var length = body.Member.Name.Length;
        var node = body;
        while (node.Expression is not null)
        {
            if (node.Expression is MemberExpression parentMember)
            {
                length += parentMember.Member.Name.Length + 1;
                node = parentMember;
            }
            else if (node.Expression is ParameterExpression)
            {
                break;
            }
            else
            {
                throw new ArgumentException(ExpressionNotRepresentableMessage);
            }
        }

        // Now construct the string
        return string.Create(length, body, (chars, body) =>
        {
            var nextPos = chars.Length;
            while (body is not null)
            {
                nextPos -= body.Member.Name.Length;
                body.Member.Name.CopyTo(chars.Slice(nextPos));
                if (nextPos > 0)
                {
                    chars[--nextPos] = '.';
                }
                body = (body.Expression as MemberExpression)!;
            }
        });
    }
}
