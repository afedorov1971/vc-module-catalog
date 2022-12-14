angular.module('virtoCommerce.catalogModule')
.directive('vaProperty2', ['$compile', '$filter', '$parse', '$templateCache', '$http', function ($compile, $filter, $parse, $templateCache, $http) {

    return {
        restrict: 'E',
        require: ['^form', 'ngModel'],
        replace: true,
        transclude: true,
        templateUrl: 'Modules/$(VirtoCommerce.Catalog)/Scripts/directives/property2.tpl.html',
        scope: {
            languages: "=",
            defaultLanguage: "=",
            getPropValues: "&"
        },
        link: function (scope, element, attr, ctrls, linker) {
            var ngModelController = ctrls[1];

            scope.currentEntity = ngModelController.$modelValue;

            scope.context = {};
            scope.context.currentPropValues = [];
            scope.context.allDictionaryValues = [];
            scope.context.langValuesMap = {};
            scope.context.form = ctrls[0];

            scope.$watch('context.langValuesMap', function (newValue, oldValue) {
                if (newValue != oldValue) {
                    scope.context.currentPropValues = [];
                    angular.forEach(scope.context.langValuesMap, function (langGroup, languageCode) {
                        angular.forEach(langGroup.currentPropValues, function (propValue) {
                            propValue.languageCode = languageCode;
                            scope.context.currentPropValues.push(propValue);
                        });
                    });
                }
            }, true);

            scope.$watch('context.currentPropValues', function (newValues) {
                //reflect only real changes
                if (isValuesDifferent(newValues, scope.currentEntity.values)) {                    
                    if (scope.currentEntity.dictionary && scope.currentEntity.multilanguage) {
                        if (scope.currentEntity.multivalue) {
                            var realAliases = _.pluck(_.where(newValues, { languageCode: scope.defaultLanguage }), 'alias');
                            scope.currentEntity.values = _.filter(scope.context.allDictionaryValues, function (x) {
                                return _.contains(realAliases, x.alias);
                            });
                        } else {
                            scope.currentEntity.values = _.where(scope.context.allDictionaryValues, { alias: newValues[0].alias });
                        }
                    } else {
                        scope.currentEntity.values = newValues;
                    }
                    	//reset inherited status to force property value override
                        _.each(scope.currentEntity.values, function (x) { x.isInherited = false; });

                    ngModelController.$setViewValue(scope.currentEntity);
                }
            }, true);


            ngModelController.$render = function () {
                scope.currentEntity = ngModelController.$modelValue;

                scope.context.currentPropValues = angular.copy(scope.currentEntity.values);
                if (needAddEmptyValue(scope.currentEntity, scope.context.currentPropValues)) {
                    scope.context.currentPropValues.push({ value: null });
                }

                if (scope.currentEntity.dictionary) {
                    loadDictionaryValues();
                }

                initLanguagesValuesMap();

                chageValueTemplate(scope.currentEntity.valueType);
            };

            function isValuesDifferent(newValues, currentValues) {
                var elementCountIsDifferent = newValues.length != currentValues.length;
                var elementsNotEqual = _.any(newValues, function (x) {
                    return _.all(currentValues, function (y) {
                        return !(y.value === x.value && y.languageCode == x.languageCode);
                    });
                });

                return (elementCountIsDifferent || elementsNotEqual) &&
                        (_.any(currentValues) || (newValues[0] && newValues[0].value)); //Prevent reflecting the change when null value was added to empty initial values
            };

            function needAddEmptyValue(property, values) {
                return !property.multivalue && !property.dictionary && values.length == 0;
            };

            function initLanguagesValuesMap() {
                if (scope.currentEntity.multilanguage) {
                    //Group values by language 
                    angular.forEach(scope.languages, function (language) {
                        //Currently select values
                        var currentPropValues = _.where(scope.context.currentPropValues, { languageCode: language });
                        // provide default value if value wasn't found in specified language.
                        if (!_.any(currentPropValues) && _.any(scope.context.currentPropValues)) {
                            currentPropValues = angular.copy(_.filter(scope.context.currentPropValues, function (x) { return !x.languageCode }));
                            _.each(currentPropValues, function (x) {
                                x.id = null;
                                x.languageCode = language;
                            });
                        }
                        //need add empty value for single  value type
                        if (needAddEmptyValue(scope.currentEntity, currentPropValues)) {
                            currentPropValues.push({ value: null, languageCode: language });
                        }
                        //All possible dict values
                        var allValues = _.where(scope.context.allDictionaryValues, { languageCode: language });

                        var langValuesGroup = {
                            allValues: allValues,
                            currentPropValues: currentPropValues
                        };
                        scope.context.langValuesMap[language] = langValuesGroup;
                    });
                }
            };

            function loadDictionaryValues() {
                scope.getPropValues()(scope.currentEntity.id).then(function (result) {
                    scope.context.allDictionaryValues = [];
                    scope.context.currentPropValues = [];

                    angular.forEach(result, function (dictValue) {
                        //Need to select already selected values. Dictionary values have same type as standard values.
                        dictValue.selected = angular.isDefined(_.find(scope.currentEntity.values, function (value) { return value.valueId == dictValue.valueId }));
                        scope.context.allDictionaryValues.push(dictValue);
                        if (dictValue.selected) {
                            //add selected value
                            scope.context.currentPropValues.push(dictValue);
                        }
                    });

                    initLanguagesValuesMap();

                    return result;
                });

            };

            function getTemplateName(property) {
                var result = property.valueType;

                if (property.dictionary) {
                    result += '-dictionary';
                }
                if (property.multivalue) {
                    result += '-multivalue';
                }
                if (property.multilanguage) {
                    result += '-multilang';
                }
                result += '.html';
                return result;
            };

            function chageValueTemplate(valueType) {
                var templateName = getTemplateName(scope.currentEntity);


                //load input template and display
                $http.get(templateName, { cache: $templateCache }).then(function (results) {
                    //Need to add ngForm to isolate form validation into sub form
                    //var innerContainer = "<div id='innerContainer' />";

                    //We must destroy scope of elements we are removing from DOM to avoid angular firing events
                    var el = element.find('#valuePlaceHolder #innerContainer');
                    if (el.length > 0) {
                        el.scope().$destroy();
                    }
                    var container = element.find('#valuePlaceHolder');
                    var result = container.html(results.data.trim());

                    //Create new scope, otherwise we would destroy our directive scope
                    var newScope = scope.$new();
                    $compile(result)(newScope);
                });
            };

            /* Datepicker */
            scope.datepickers = {
                DateTime: false
            }
            scope.open = function ($event, which) {
                $event.preventDefault();
                $event.stopPropagation();

                scope.datepickers[which] = true;
            };

            scope.dateOptions = {
            	datepickerMode: 'day'
            };

            linker(function (clone) {
                element.append(clone);
            });

            function setValid(name) {
                var form = scope.context.form;
                form[name].$setValidity('minlength', true);
                form[name].$setValidity('maxlength', true);
                form[name].$setValidity('pattern', true);
            }

            scope.tagsDeleted = function (tag, name, required) {
                var form = scope.context.form;
                var values = scope.context.currentPropValues;
                if (required && values.length === 0) {
                    form[name].$setValidity('required', false);
                }

                setValid(name);
            };

            scope.tagsAdded = function (tag, name) {
                var form = scope.context.form;
                if (tag.value) {
                    form[name].$setValidity('required', true);
                }

                setValid(name);
            };

            scope.addederror = function(tag, name, minValue, maxValue, pattern) {
                var form = scope.context.form;
                if (minValue && tag.value.length < minValue) {
                    form[name].$setValidity('minlength', false);
                }

                if (maxValue && tag.value.length > maxValue) {
                    form[name].$setValidity('maxlength', false);
                }

                if (pattern) {
                    var re = new RegExp(pattern);
                    if (!re.test(tag.value)) {
                        form[name].$setValidity('pattern', false);
                    }
                }
            };
        }
    }
}]);